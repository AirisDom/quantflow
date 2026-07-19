import os
import grpc
from concurrent import futures
import numpy as np
import time
import logging
import sys
from datetime import datetime, timezone
from pythonjsonlogger import jsonlogger

import quantflow_pb2
import quantflow_pb2_grpc


class QuantFlowJsonFormatter(jsonlogger.JsonFormatter):
    def add_fields(self, log_record, record, message_dict):
        super().add_fields(log_record, record, message_dict)
        log_record['@t'] = datetime.now(timezone.utc).isoformat()
        log_record['@l'] = record.levelname
        log_record['@m'] = log_record.pop('message', record.getMessage())
        log_record['Service'] = 'QuantFlow.SignalEngine'
        log_record['Version'] = '1.0.0'

        if record.name:
            log_record['SourceContext'] = record.name

        if record.exc_info:
            log_record['@x'] = self.formatException(record.exc_info)


def setup_logging():
    log_level = os.environ.get("SIGNAL_ENGINE_LOG_LEVEL", "INFO").upper()

    handler = logging.StreamHandler(sys.stdout)
    handler.setFormatter(QuantFlowJsonFormatter())

    root_logger = logging.getLogger()
    root_logger.handlers.clear()
    root_logger.addHandler(handler)
    root_logger.setLevel(getattr(logging, log_level, logging.INFO))

    logging.getLogger('grpc').setLevel(logging.WARNING)

    return logging.getLogger("SignalEngine")


logger = setup_logging()


class RollingPriceWindow:
    def __init__(self, window_size: int = 20):
        self.window_size = window_size
        self.prices = np.array([], dtype=np.float64)

    def add_price(self, price: float) -> None:
        self.prices = np.append(self.prices, price)
        if len(self.prices) > self.window_size:
            self.prices = self.prices[-self.window_size:]

    def get_prices(self) -> np.ndarray:
        return self.prices

    def calculate_moving_average(self) -> float:
        if len(self.prices) == 0:
            return 0.0
        return float(np.mean(self.prices))

    def get_current_price(self) -> float:
        if len(self.prices) == 0:
            return 0.0
        return float(self.prices[-1])

    def __len__(self) -> int:
        return len(self.prices)


def calculate_signal_and_confidence(
    current_price: float, moving_average: float, threshold: float = 0.02
) -> tuple[quantflow_pb2.SignalType, float]:
    if moving_average == 0.0:
        return quantflow_pb2.HOLD, 0.0

    deviation = (current_price - moving_average) / moving_average

    if deviation <= -threshold:
        confidence = min(abs(deviation) / threshold, 2.0) / 2.0
        return quantflow_pb2.BUY, confidence
    elif deviation >= threshold:
        confidence = min(abs(deviation) / threshold, 2.0) / 2.0
        return quantflow_pb2.SELL, confidence
    else:
        confidence = 1.0 - (abs(deviation) / threshold)
        return quantflow_pb2.HOLD, confidence


class SignalServiceServicer(quantflow_pb2_grpc.SignalServiceServicer):
    def __init__(self, window_size: int = 20, threshold: float = 0.02):
        self.window_size = window_size
        self.threshold = threshold
        self.price_windows: dict[str, RollingPriceWindow] = {}
        logger.info(
            "SignalServiceServicer initialized",
            extra={"WindowSize": window_size, "Threshold": threshold}
        )

    def GetSignal(self, request_iterator, context):
        correlation_id = None
        for key, value in context.invocation_metadata():
            if key.lower() == 'x-correlation-id':
                correlation_id = value
                break

        asset = None
        tick_count = 0

        for tick in request_iterator:
            asset = tick.asset
            if asset not in self.price_windows:
                self.price_windows[asset] = RollingPriceWindow(self.window_size)
                logger.info(
                    "Created new price window",
                    extra={"Asset": asset, "CorrelationId": correlation_id}
                )
            self.price_windows[asset].add_price(tick.price)
            tick_count += 1

        timestamp_ms = int(time.time() * 1000)

        if not asset or asset not in self.price_windows:
            logger.warning(
                "No valid asset received in price ticks",
                extra={"CorrelationId": correlation_id}
            )
            return quantflow_pb2.TradeSignal(
                asset="",
                signal=quantflow_pb2.HOLD,
                timestamp=timestamp_ms,
                confidence=0.0
            )

        window = self.price_windows[asset]
        if len(window) == 0:
            logger.warning(
                "Empty price window",
                extra={"Asset": asset, "CorrelationId": correlation_id}
            )
            return quantflow_pb2.TradeSignal(
                asset=asset,
                signal=quantflow_pb2.HOLD,
                timestamp=timestamp_ms,
                confidence=0.0
            )

        current_price = window.get_current_price()
        moving_average = window.calculate_moving_average()
        signal_type, confidence = calculate_signal_and_confidence(
            current_price, moving_average, self.threshold
        )

        signal_name = {
            quantflow_pb2.HOLD: "HOLD",
            quantflow_pb2.BUY: "BUY",
            quantflow_pb2.SELL: "SELL"
        }.get(signal_type, "UNKNOWN")

        logger.info(
            "Signal generated",
            extra={
                "Asset": asset,
                "Signal": signal_name,
                "Price": round(current_price, 4),
                "MovingAverage": round(moving_average, 4),
                "Confidence": round(confidence, 4),
                "TickCount": tick_count,
                "CorrelationId": correlation_id
            }
        )

        return quantflow_pb2.TradeSignal(
            asset=asset,
            signal=signal_type,
            timestamp=timestamp_ms,
            confidence=confidence
        )


def serve(port: int = 50051, window_size: int = 20, threshold: float = 0.02):
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=10))
    quantflow_pb2_grpc.add_SignalServiceServicer_to_server(
        SignalServiceServicer(window_size=window_size, threshold=threshold), server
    )
    server.add_insecure_port(f"[::]:{port}")
    server.start()
    logger.info(
        "Signal Engine gRPC server started",
        extra={"Port": port, "WindowSize": window_size, "Threshold": threshold}
    )
    try:
        while True:
            time.sleep(86400)
    except KeyboardInterrupt:
        logger.info("Shutting down Signal Engine")
        server.stop(0)


if __name__ == "__main__":
    port = int(os.environ.get("SIGNAL_ENGINE_PORT", "50051"))
    window_size = int(os.environ.get("SIGNAL_ENGINE_WINDOW_SIZE", "20"))
    threshold = float(os.environ.get("SIGNAL_ENGINE_THRESHOLD", "0.02"))
    serve(port=port, window_size=window_size, threshold=threshold)

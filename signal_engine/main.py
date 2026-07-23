import os
import grpc
from concurrent import futures
import numpy as np
import time
import logging
import sys
import signal
import threading
import asyncio
import json
from datetime import datetime, timezone
from http.server import HTTPServer, BaseHTTPRequestHandler
from pythonjsonlogger import jsonlogger
from prometheus_client import Counter, Histogram, Gauge, generate_latest, CONTENT_TYPE_LATEST

import quantflow_pb2
import quantflow_pb2_grpc

SIGNALS_GENERATED_TOTAL = Counter(
    'signal_engine_signals_generated_total',
    'Total number of signals generated',
    ['asset', 'signal_type']
)

PRICE_TICKS_RECEIVED_TOTAL = Counter(
    'signal_engine_price_ticks_received_total',
    'Total number of price ticks received',
    ['asset']
)

SIGNAL_PROCESSING_LATENCY = Histogram(
    'signal_engine_processing_latency_seconds',
    'Signal processing latency in seconds',
    buckets=[0.0001, 0.0005, 0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1.0]
)

ACTIVE_PRICE_WINDOWS = Gauge(
    'signal_engine_active_price_windows',
    'Number of active price windows being tracked'
)


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
        start_time = time.perf_counter()
        correlation_id = None
        for key, value in context.invocation_metadata():
            if key.lower() == 'x-correlation-id':
                correlation_id = value
                break

        asset = None
        tick_count = 0

        for tick in request_iterator:
            asset = tick.asset
            PRICE_TICKS_RECEIVED_TOTAL.labels(asset=asset).inc()
            if asset not in self.price_windows:
                self.price_windows[asset] = RollingPriceWindow(self.window_size)
                ACTIVE_PRICE_WINDOWS.inc()
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
            SIGNAL_PROCESSING_LATENCY.observe(time.perf_counter() - start_time)
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
            SIGNAL_PROCESSING_LATENCY.observe(time.perf_counter() - start_time)
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

        SIGNALS_GENERATED_TOTAL.labels(asset=asset, signal_type=signal_name).inc()
        SIGNAL_PROCESSING_LATENCY.observe(time.perf_counter() - start_time)

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


class GracefulShutdownHandler:
    def __init__(self, server: grpc.Server, grace_period: float = 5.0):
        self._server = server
        self._grace_period = grace_period
        self._shutdown_event = threading.Event()
        self._is_shutting_down = False

    def register_signals(self):
        signal.signal(signal.SIGTERM, self._handle_signal)
        signal.signal(signal.SIGINT, self._handle_signal)
        logger.debug("Registered SIGTERM and SIGINT handlers")

    def _handle_signal(self, signum, frame):
        signal_name = signal.Signals(signum).name
        logger.info(
            "Received shutdown signal",
            extra={"Signal": signal_name, "GracePeriod": self._grace_period}
        )
        self._is_shutting_down = True
        self._initiate_shutdown()

    def _initiate_shutdown(self):
        logger.info(
            "Initiating graceful shutdown",
            extra={"GracePeriod": self._grace_period}
        )
        shutdown_event = self._server.stop(self._grace_period)

        def wait_for_shutdown():
            shutdown_event.wait()
            logger.info("gRPC server stopped, all connections drained")
            self._shutdown_event.set()

        threading.Thread(target=wait_for_shutdown, daemon=True).start()

    def wait_for_termination(self):
        try:
            while not self._shutdown_event.is_set():
                time.sleep(0.5)
        except KeyboardInterrupt:
            if not self._is_shutting_down:
                logger.info("Received KeyboardInterrupt, initiating shutdown")
                self._initiate_shutdown()
                self._shutdown_event.wait(timeout=self._grace_period + 1)

    @property
    def is_shutting_down(self) -> bool:
        return self._is_shutting_down


class HealthCheckHandler(BaseHTTPRequestHandler):
    shutdown_handler: 'GracefulShutdownHandler | None' = None

    def log_message(self, format, *args):
        pass

    def do_GET(self):
        if self.path == '/health':
            self._handle_health()
        elif self.path == '/ready':
            self._handle_ready()
        elif self.path == '/metrics':
            self._handle_metrics()
        else:
            self.send_error(404, "Not Found")

    def _handle_metrics(self):
        metrics_output = generate_latest()
        self.send_response(200)
        self.send_header('Content-Type', CONTENT_TYPE_LATEST)
        self.end_headers()
        self.wfile.write(metrics_output)

    def _handle_health(self):
        is_shutting_down = (
            self.shutdown_handler is not None and self.shutdown_handler.is_shutting_down
        )
        status = "ShuttingDown" if is_shutting_down else "Healthy"
        status_code = 503 if is_shutting_down else 200
        response = {
            "status": status,
            "service": "QuantFlow.SignalEngine",
            "timestamp": datetime.now(timezone.utc).isoformat()
        }
        self._send_json_response(status_code, response)

    def _handle_ready(self):
        is_shutting_down = (
            self.shutdown_handler is not None and self.shutdown_handler.is_shutting_down
        )
        status = "ShuttingDown" if is_shutting_down else "Ready"
        status_code = 503 if is_shutting_down else 200
        response = {
            "status": status,
            "service": "QuantFlow.SignalEngine",
            "timestamp": datetime.now(timezone.utc).isoformat()
        }
        self._send_json_response(status_code, response)

    def _send_json_response(self, status_code: int, data: dict):
        self.send_response(status_code)
        self.send_header('Content-Type', 'application/json')
        self.end_headers()
        self.wfile.write(json.dumps(data).encode('utf-8'))


def start_health_server(port: int, shutdown_handler: GracefulShutdownHandler):
    HealthCheckHandler.shutdown_handler = shutdown_handler
    server = HTTPServer(('0.0.0.0', port), HealthCheckHandler)
    server.timeout = 1.0

    def run():
        logger.info("Health HTTP server started", extra={"Port": port})
        while not shutdown_handler.is_shutting_down:
            server.handle_request()
        server.server_close()
        logger.info("Health HTTP server stopped")

    thread = threading.Thread(target=run, daemon=True)
    thread.start()
    return server


def serve(port: int = 50051, window_size: int = 20, threshold: float = 0.02):
    grace_period = float(os.environ.get("SIGNAL_ENGINE_SHUTDOWN_GRACE_PERIOD", "5.0"))
    health_port = int(os.environ.get("SIGNAL_ENGINE_HEALTH_PORT", "8080"))

    server = grpc.server(futures.ThreadPoolExecutor(max_workers=10))
    quantflow_pb2_grpc.add_SignalServiceServicer_to_server(
        SignalServiceServicer(window_size=window_size, threshold=threshold), server
    )
    server.add_insecure_port(f"[::]:{port}")

    shutdown_handler = GracefulShutdownHandler(server, grace_period)
    shutdown_handler.register_signals()

    start_health_server(health_port, shutdown_handler)

    server.start()
    logger.info(
        "Signal Engine gRPC server started",
        extra={"Port": port, "HealthPort": health_port, "WindowSize": window_size, "Threshold": threshold}
    )

    shutdown_handler.wait_for_termination()
    logger.info("Signal Engine shutdown complete")


if __name__ == "__main__":
    port = int(os.environ.get("SIGNAL_ENGINE_PORT", "50051"))
    window_size = int(os.environ.get("SIGNAL_ENGINE_WINDOW_SIZE", "20"))
    threshold = float(os.environ.get("SIGNAL_ENGINE_THRESHOLD", "0.02"))
    serve(port=port, window_size=window_size, threshold=threshold)

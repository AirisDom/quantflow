import os
import grpc
from concurrent import futures
import numpy as np
import time

import quantflow_pb2
import quantflow_pb2_grpc


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


class SignalServiceServicer(quantflow_pb2_grpc.SignalServiceServicer):
    def __init__(self, window_size: int = 20):
        self.window_size = window_size
        self.price_windows: dict[str, RollingPriceWindow] = {}

    def GetSignal(self, request_iterator, context):
        asset = None

        for tick in request_iterator:
            asset = tick.asset
            if asset not in self.price_windows:
                self.price_windows[asset] = RollingPriceWindow(self.window_size)
            self.price_windows[asset].add_price(tick.price)

        if not asset or asset not in self.price_windows:
            return quantflow_pb2.TradeSignal(asset="", signal=quantflow_pb2.HOLD)

        window = self.price_windows[asset]
        if len(window) == 0:
            return quantflow_pb2.TradeSignal(asset=asset, signal=quantflow_pb2.HOLD)

        current_price = window.get_current_price()
        moving_average = window.calculate_moving_average()

        threshold = 0.02
        signal = quantflow_pb2.HOLD

        if current_price < moving_average * (1 - threshold):
            signal = quantflow_pb2.BUY
        elif current_price > moving_average * (1 + threshold):
            signal = quantflow_pb2.SELL

        return quantflow_pb2.TradeSignal(asset=asset, signal=signal)


def serve(port: int = 50051, window_size: int = 20):
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=10))
    quantflow_pb2_grpc.add_SignalServiceServicer_to_server(
        SignalServiceServicer(window_size=window_size), server
    )
    server.add_insecure_port(f"[::]:{port}")
    server.start()
    print(f"Signal Engine gRPC server started on port {port}")
    try:
        while True:
            time.sleep(86400)
    except KeyboardInterrupt:
        server.stop(0)


if __name__ == "__main__":
    port = int(os.environ.get("SIGNAL_ENGINE_PORT", "50051"))
    window_size = int(os.environ.get("SIGNAL_ENGINE_WINDOW_SIZE", "20"))
    serve(port=port, window_size=window_size)

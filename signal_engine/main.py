import grpc
from concurrent import futures
import numpy as np
import time

import quantflow_pb2
import quantflow_pb2_grpc


class SignalServiceServicer(quantflow_pb2_grpc.SignalServiceServicer):
    def __init__(self, window_size: int = 20):
        self.window_size = window_size
        self.price_windows: dict[str, list[float]] = {}

    def GetSignal(self, request_iterator, context):
        asset = None
        prices: list[float] = []

        for tick in request_iterator:
            asset = tick.asset
            if asset not in self.price_windows:
                self.price_windows[asset] = []

            self.price_windows[asset].append(tick.price)
            if len(self.price_windows[asset]) > self.window_size:
                self.price_windows[asset].pop(0)

            prices = self.price_windows[asset]

        if not asset or len(prices) == 0:
            return quantflow_pb2.TradeSignal(asset="", signal=quantflow_pb2.HOLD)

        current_price = prices[-1]
        moving_average = np.mean(prices)

        threshold = 0.02
        signal = quantflow_pb2.HOLD

        if current_price < moving_average * (1 - threshold):
            signal = quantflow_pb2.BUY
        elif current_price > moving_average * (1 + threshold):
            signal = quantflow_pb2.SELL

        return quantflow_pb2.TradeSignal(asset=asset, signal=signal)


def serve(port: int = 50052):
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=10))
    quantflow_pb2_grpc.add_SignalServiceServicer_to_server(
        SignalServiceServicer(), server
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
    serve()

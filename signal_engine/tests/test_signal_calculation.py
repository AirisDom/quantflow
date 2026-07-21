import pytest
import numpy as np

from main import RollingPriceWindow, calculate_signal_and_confidence
import quantflow_pb2


class TestRollingPriceWindow:
    def test_add_price_stores_prices(self):
        window = RollingPriceWindow(window_size=5)
        window.add_price(100.0)
        window.add_price(101.0)
        window.add_price(102.0)

        assert len(window) == 3
        np.testing.assert_array_equal(window.get_prices(), [100.0, 101.0, 102.0])

    def test_window_size_limit(self):
        window = RollingPriceWindow(window_size=3)
        for price in [100.0, 101.0, 102.0, 103.0, 104.0]:
            window.add_price(price)

        assert len(window) == 3
        np.testing.assert_array_equal(window.get_prices(), [102.0, 103.0, 104.0])

    def test_moving_average_calculation(self):
        window = RollingPriceWindow(window_size=5)
        for price in [100.0, 102.0, 98.0, 104.0, 96.0]:
            window.add_price(price)

        expected_avg = (100.0 + 102.0 + 98.0 + 104.0 + 96.0) / 5
        assert window.calculate_moving_average() == pytest.approx(expected_avg)
        assert window.calculate_moving_average() == pytest.approx(100.0)

    def test_moving_average_partial_window(self):
        window = RollingPriceWindow(window_size=10)
        window.add_price(50.0)
        window.add_price(100.0)
        window.add_price(150.0)

        assert window.calculate_moving_average() == pytest.approx(100.0)

    def test_empty_window_moving_average(self):
        window = RollingPriceWindow(window_size=5)

        assert window.calculate_moving_average() == 0.0

    def test_get_current_price(self):
        window = RollingPriceWindow(window_size=5)
        window.add_price(100.0)
        window.add_price(105.0)
        window.add_price(110.0)

        assert window.get_current_price() == 110.0

    def test_empty_window_current_price(self):
        window = RollingPriceWindow(window_size=5)

        assert window.get_current_price() == 0.0


class TestCalculateSignalAndConfidence:
    def test_buy_signal_at_minus_two_percent(self):
        moving_average = 100.0
        current_price = 98.0  # exactly -2%

        signal, confidence = calculate_signal_and_confidence(
            current_price, moving_average, threshold=0.02
        )

        assert signal == quantflow_pb2.BUY
        assert confidence > 0.0

    def test_buy_signal_below_minus_two_percent(self):
        moving_average = 100.0
        current_price = 95.0  # -5%, well below threshold

        signal, confidence = calculate_signal_and_confidence(
            current_price, moving_average, threshold=0.02
        )

        assert signal == quantflow_pb2.BUY
        assert confidence == pytest.approx(1.0)  # capped confidence

    def test_sell_signal_at_plus_two_percent(self):
        moving_average = 100.0
        current_price = 102.0  # exactly +2%

        signal, confidence = calculate_signal_and_confidence(
            current_price, moving_average, threshold=0.02
        )

        assert signal == quantflow_pb2.SELL
        assert confidence > 0.0

    def test_sell_signal_above_plus_two_percent(self):
        moving_average = 100.0
        current_price = 105.0  # +5%, well above threshold

        signal, confidence = calculate_signal_and_confidence(
            current_price, moving_average, threshold=0.02
        )

        assert signal == quantflow_pb2.SELL
        assert confidence == pytest.approx(1.0)  # capped confidence

    def test_hold_signal_in_neutral_range(self):
        moving_average = 100.0
        current_price = 100.5  # +0.5%, within threshold

        signal, confidence = calculate_signal_and_confidence(
            current_price, moving_average, threshold=0.02
        )

        assert signal == quantflow_pb2.HOLD

    def test_hold_signal_at_exact_average(self):
        moving_average = 100.0
        current_price = 100.0  # exactly at average

        signal, confidence = calculate_signal_and_confidence(
            current_price, moving_average, threshold=0.02
        )

        assert signal == quantflow_pb2.HOLD
        assert confidence == pytest.approx(1.0)  # max confidence for HOLD

    def test_hold_signal_just_below_buy_threshold(self):
        moving_average = 100.0
        current_price = 98.1  # -1.9%, just above -2%

        signal, confidence = calculate_signal_and_confidence(
            current_price, moving_average, threshold=0.02
        )

        assert signal == quantflow_pb2.HOLD

    def test_hold_signal_just_below_sell_threshold(self):
        moving_average = 100.0
        current_price = 101.9  # +1.9%, just below +2%

        signal, confidence = calculate_signal_and_confidence(
            current_price, moving_average, threshold=0.02
        )

        assert signal == quantflow_pb2.HOLD

    def test_zero_moving_average_returns_hold(self):
        signal, confidence = calculate_signal_and_confidence(
            current_price=100.0, moving_average=0.0, threshold=0.02
        )

        assert signal == quantflow_pb2.HOLD
        assert confidence == 0.0

    def test_zero_prices_returns_hold(self):
        signal, confidence = calculate_signal_and_confidence(
            current_price=0.0, moving_average=0.0, threshold=0.02
        )

        assert signal == quantflow_pb2.HOLD
        assert confidence == 0.0

    def test_custom_threshold(self):
        moving_average = 100.0
        current_price = 95.0  # -5%

        signal_5pct, _ = calculate_signal_and_confidence(
            current_price, moving_average, threshold=0.05
        )
        assert signal_5pct == quantflow_pb2.BUY

        signal_10pct, _ = calculate_signal_and_confidence(
            current_price, moving_average, threshold=0.10
        )
        assert signal_10pct == quantflow_pb2.HOLD

    def test_confidence_increases_with_deviation(self):
        moving_average = 100.0

        _, conf_2pct = calculate_signal_and_confidence(98.0, moving_average)
        _, conf_3pct = calculate_signal_and_confidence(97.0, moving_average)
        _, conf_4pct = calculate_signal_and_confidence(96.0, moving_average)

        assert conf_3pct > conf_2pct
        assert conf_4pct > conf_3pct


class TestEdgeCases:
    def test_insufficient_data_for_reliable_average(self):
        window = RollingPriceWindow(window_size=20)
        window.add_price(100.0)

        assert len(window) == 1
        assert window.calculate_moving_average() == 100.0

        signal, confidence = calculate_signal_and_confidence(
            window.get_current_price(), window.calculate_moving_average()
        )
        assert signal == quantflow_pb2.HOLD

    def test_all_same_prices(self):
        window = RollingPriceWindow(window_size=5)
        for _ in range(5):
            window.add_price(100.0)

        assert window.calculate_moving_average() == 100.0
        signal, confidence = calculate_signal_and_confidence(
            window.get_current_price(), window.calculate_moving_average()
        )
        assert signal == quantflow_pb2.HOLD
        assert confidence == pytest.approx(1.0)

    def test_very_small_prices(self):
        window = RollingPriceWindow(window_size=5)
        for price in [0.0001, 0.0002, 0.0001, 0.0002, 0.0001]:
            window.add_price(price)

        ma = window.calculate_moving_average()
        assert ma > 0.0

        signal, _ = calculate_signal_and_confidence(
            window.get_current_price(), ma
        )
        assert signal in [quantflow_pb2.HOLD, quantflow_pb2.BUY, quantflow_pb2.SELL]

    def test_large_price_values(self):
        window = RollingPriceWindow(window_size=5)
        base_price = 50000.0  # e.g., BTC-like price
        for price in [base_price, base_price * 1.01, base_price * 0.99,
                      base_price * 1.02, base_price * 0.98]:
            window.add_price(price)

        ma = window.calculate_moving_average()
        current = window.get_current_price()

        signal, _ = calculate_signal_and_confidence(current, ma)
        assert signal in [quantflow_pb2.HOLD, quantflow_pb2.BUY, quantflow_pb2.SELL]

    def test_negative_prices_technically_supported(self):
        window = RollingPriceWindow(window_size=3)
        window.add_price(-10.0)
        window.add_price(-20.0)
        window.add_price(-15.0)

        assert window.calculate_moving_average() == pytest.approx(-15.0)

    def test_window_size_one(self):
        window = RollingPriceWindow(window_size=1)
        window.add_price(100.0)
        window.add_price(105.0)

        assert len(window) == 1
        assert window.get_current_price() == 105.0
        assert window.calculate_moving_average() == 105.0

    def test_mixed_price_sequence_signal_generation(self):
        window = RollingPriceWindow(window_size=5)
        prices = [100.0, 100.0, 100.0, 100.0, 100.0]
        for p in prices:
            window.add_price(p)

        ma = window.calculate_moving_average()

        window.add_price(95.0)  # -5% drop
        new_ma = window.calculate_moving_average()
        signal, _ = calculate_signal_and_confidence(
            window.get_current_price(), new_ma
        )
        assert signal == quantflow_pb2.BUY

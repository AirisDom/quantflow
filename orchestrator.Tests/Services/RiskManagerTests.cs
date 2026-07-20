using Microsoft.Extensions.Options;
using QuantFlow.Orchestrator.Configuration;
using QuantFlow.Orchestrator.Services;

namespace QuantFlow.Orchestrator.Tests.Services;

public class RiskManagerTests
{
    private static RiskManager CreateRiskManager(
        decimal maxDrawdownPercent = 0.05m,
        decimal maxPositionSizePercent = 0.10m,
        decimal maxExposurePercent = 0.80m,
        decimal minOrderValue = 10m)
    {
        var settings = new RiskSettings
        {
            MaxDrawdownPercent = maxDrawdownPercent,
            MaxPositionSizePercent = maxPositionSizePercent,
            MaxExposurePercent = maxExposurePercent,
            MinOrderValue = minOrderValue
        };
        return new RiskManager(Options.Create(settings));
    }

    private static PortfolioState CreatePortfolioState(
        decimal totalEquity = 100_000m,
        decimal peakEquity = 100_000m,
        Dictionary<string, decimal>? positions = null,
        decimal totalExposure = 0m)
    {
        return new PortfolioState(
            totalEquity,
            peakEquity,
            positions ?? new Dictionary<string, decimal>(),
            totalExposure);
    }

    public class CalculateDrawdownTests
    {
        [Fact]
        public void ReturnsZero_WhenPeakEquityIsZero()
        {
            var riskManager = CreateRiskManager();
            var result = riskManager.CalculateDrawdown(100m, 0m);
            Assert.Equal(0m, result);
        }

        [Fact]
        public void ReturnsZero_WhenPeakEquityIsNegative()
        {
            var riskManager = CreateRiskManager();
            var result = riskManager.CalculateDrawdown(100m, -100m);
            Assert.Equal(0m, result);
        }

        [Fact]
        public void ReturnsZero_WhenCurrentEquityEqualsPeakEquity()
        {
            var riskManager = CreateRiskManager();
            var result = riskManager.CalculateDrawdown(100_000m, 100_000m);
            Assert.Equal(0m, result);
        }

        [Fact]
        public void ReturnsZero_WhenCurrentEquityExceedsPeakEquity()
        {
            var riskManager = CreateRiskManager();
            var result = riskManager.CalculateDrawdown(110_000m, 100_000m);
            Assert.Equal(0m, result);
        }

        [Fact]
        public void CalculatesCorrectDrawdown_WhenCurrentEquityIsLower()
        {
            var riskManager = CreateRiskManager();
            var result = riskManager.CalculateDrawdown(95_000m, 100_000m);
            Assert.Equal(0.05m, result);
        }

        [Fact]
        public void CalculatesCorrectDrawdown_ForLargeDrawdown()
        {
            var riskManager = CreateRiskManager();
            var result = riskManager.CalculateDrawdown(50_000m, 100_000m);
            Assert.Equal(0.50m, result);
        }

        [Fact]
        public void CalculatesCorrectDrawdown_ForSmallDrawdown()
        {
            var riskManager = CreateRiskManager();
            var result = riskManager.CalculateDrawdown(99_000m, 100_000m);
            Assert.Equal(0.01m, result);
        }
    }

    public class ValidatePositionSizeTests
    {
        [Fact]
        public void ReturnsFalse_WhenTotalEquityIsZero()
        {
            var riskManager = CreateRiskManager();
            var result = riskManager.ValidatePositionSize(10m, 100m, 0m);
            Assert.False(result);
        }

        [Fact]
        public void ReturnsFalse_WhenTotalEquityIsNegative()
        {
            var riskManager = CreateRiskManager();
            var result = riskManager.ValidatePositionSize(10m, 100m, -1000m);
            Assert.False(result);
        }

        [Fact]
        public void ReturnsTrue_WhenPositionSizeIsAtLimit()
        {
            var riskManager = CreateRiskManager(maxPositionSizePercent: 0.10m);
            var result = riskManager.ValidatePositionSize(100m, 100m, 100_000m);
            Assert.True(result);
        }

        [Fact]
        public void ReturnsTrue_WhenPositionSizeIsBelowLimit()
        {
            var riskManager = CreateRiskManager(maxPositionSizePercent: 0.10m);
            var result = riskManager.ValidatePositionSize(50m, 100m, 100_000m);
            Assert.True(result);
        }

        [Fact]
        public void ReturnsFalse_WhenPositionSizeExceedsLimit()
        {
            var riskManager = CreateRiskManager(maxPositionSizePercent: 0.10m);
            var result = riskManager.ValidatePositionSize(150m, 100m, 100_000m);
            Assert.False(result);
        }

        [Fact]
        public void HandlesSmallPositionSizePercent()
        {
            var riskManager = CreateRiskManager(maxPositionSizePercent: 0.01m);
            var result = riskManager.ValidatePositionSize(10m, 100m, 100_000m);
            Assert.True(result);
        }

        [Fact]
        public void HandlesLargePositionSizePercent()
        {
            var riskManager = CreateRiskManager(maxPositionSizePercent: 0.50m);
            var result = riskManager.ValidatePositionSize(400m, 100m, 100_000m);
            Assert.True(result);
        }
    }

    public class ValidateExposureTests
    {
        [Fact]
        public void ReturnsFalse_WhenTotalEquityIsZero()
        {
            var riskManager = CreateRiskManager();
            var result = riskManager.ValidateExposure(0m, 1000m, 0m);
            Assert.False(result);
        }

        [Fact]
        public void ReturnsFalse_WhenTotalEquityIsNegative()
        {
            var riskManager = CreateRiskManager();
            var result = riskManager.ValidateExposure(0m, 1000m, -1000m);
            Assert.False(result);
        }

        [Fact]
        public void ReturnsTrue_WhenProjectedExposureIsAtLimit()
        {
            var riskManager = CreateRiskManager(maxExposurePercent: 0.80m);
            var result = riskManager.ValidateExposure(70_000m, 10_000m, 100_000m);
            Assert.True(result);
        }

        [Fact]
        public void ReturnsTrue_WhenProjectedExposureIsBelowLimit()
        {
            var riskManager = CreateRiskManager(maxExposurePercent: 0.80m);
            var result = riskManager.ValidateExposure(0m, 50_000m, 100_000m);
            Assert.True(result);
        }

        [Fact]
        public void ReturnsFalse_WhenProjectedExposureExceedsLimit()
        {
            var riskManager = CreateRiskManager(maxExposurePercent: 0.80m);
            var result = riskManager.ValidateExposure(75_000m, 10_000m, 100_000m);
            Assert.False(result);
        }

        [Fact]
        public void HandlesZeroCurrentExposure()
        {
            var riskManager = CreateRiskManager(maxExposurePercent: 0.80m);
            var result = riskManager.ValidateExposure(0m, 80_000m, 100_000m);
            Assert.True(result);
        }

        [Fact]
        public void HandlesMaxedOutExposure()
        {
            var riskManager = CreateRiskManager(maxExposurePercent: 0.80m);
            var result = riskManager.ValidateExposure(80_000m, 1m, 100_000m);
            Assert.False(result);
        }
    }

    public class EvaluateTradeTests
    {
        [Fact]
        public void RejectsTradeWithZeroQuantity()
        {
            var riskManager = CreateRiskManager();
            var proposal = new TradeProposal("BTC", 0m, 50000m, TradeSide.Buy);
            var portfolio = CreatePortfolioState();

            var result = riskManager.EvaluateTrade(proposal, portfolio);

            Assert.False(result.IsApproved);
            Assert.Contains("Invalid quantity", result.Reason);
        }

        [Fact]
        public void RejectsTradeWithNegativeQuantity()
        {
            var riskManager = CreateRiskManager();
            var proposal = new TradeProposal("BTC", -1m, 50000m, TradeSide.Buy);
            var portfolio = CreatePortfolioState();

            var result = riskManager.EvaluateTrade(proposal, portfolio);

            Assert.False(result.IsApproved);
            Assert.Contains("Invalid quantity", result.Reason);
        }

        [Fact]
        public void RejectsTradeWithZeroPrice()
        {
            var riskManager = CreateRiskManager();
            var proposal = new TradeProposal("BTC", 1m, 0m, TradeSide.Buy);
            var portfolio = CreatePortfolioState();

            var result = riskManager.EvaluateTrade(proposal, portfolio);

            Assert.False(result.IsApproved);
            Assert.Contains("Invalid price", result.Reason);
        }

        [Fact]
        public void RejectsTradeWithNegativePrice()
        {
            var riskManager = CreateRiskManager();
            var proposal = new TradeProposal("BTC", 1m, -100m, TradeSide.Buy);
            var portfolio = CreatePortfolioState();

            var result = riskManager.EvaluateTrade(proposal, portfolio);

            Assert.False(result.IsApproved);
            Assert.Contains("Invalid price", result.Reason);
        }

        [Fact]
        public void RejectsTradeWhenOrderValueBelowMinimum()
        {
            var riskManager = CreateRiskManager(minOrderValue: 100m);
            var proposal = new TradeProposal("BTC", 0.001m, 50m, TradeSide.Buy);
            var portfolio = CreatePortfolioState();

            var result = riskManager.EvaluateTrade(proposal, portfolio);

            Assert.False(result.IsApproved);
            Assert.Contains("below minimum", result.Reason);
        }

        [Fact]
        public void RejectsTradeWhenMaxDrawdownReached()
        {
            var riskManager = CreateRiskManager(maxDrawdownPercent: 0.05m);
            var proposal = new TradeProposal("BTC", 0.1m, 50000m, TradeSide.Buy);
            var portfolio = CreatePortfolioState(
                totalEquity: 94_000m,
                peakEquity: 100_000m);

            var result = riskManager.EvaluateTrade(proposal, portfolio);

            Assert.False(result.IsApproved);
            Assert.Contains("Max drawdown limit reached", result.Reason);
        }

        [Fact]
        public void RejectsTradeWhenDrawdownExactlyAtLimit()
        {
            var riskManager = CreateRiskManager(maxDrawdownPercent: 0.05m);
            var proposal = new TradeProposal("BTC", 0.1m, 50000m, TradeSide.Buy);
            var portfolio = CreatePortfolioState(
                totalEquity: 95_000m,
                peakEquity: 100_000m);

            var result = riskManager.EvaluateTrade(proposal, portfolio);

            Assert.False(result.IsApproved);
            Assert.Contains("Max drawdown limit reached", result.Reason);
        }

        [Fact]
        public void RejectsTradeWhenPositionSizeExceedsLimit()
        {
            var riskManager = CreateRiskManager(maxPositionSizePercent: 0.10m);
            var proposal = new TradeProposal("BTC", 0.5m, 50000m, TradeSide.Buy);
            var portfolio = CreatePortfolioState(totalEquity: 100_000m);

            var result = riskManager.EvaluateTrade(proposal, portfolio);

            Assert.False(result.IsApproved);
            Assert.Contains("Position size", result.Reason);
            Assert.Contains("exceeds limit", result.Reason);
        }

        [Fact]
        public void RejectsBuyWhenTotalExposureWouldExceedLimit()
        {
            var riskManager = CreateRiskManager(maxExposurePercent: 0.80m);
            var proposal = new TradeProposal("BTC", 0.1m, 50000m, TradeSide.Buy);
            var portfolio = CreatePortfolioState(
                totalEquity: 100_000m,
                totalExposure: 78_000m);

            var result = riskManager.EvaluateTrade(proposal, portfolio);

            Assert.False(result.IsApproved);
            Assert.Contains("exposure would exceed", result.Reason);
        }

        [Fact]
        public void RejectsSellWhenInsufficientPosition()
        {
            var riskManager = CreateRiskManager();
            var positions = new Dictionary<string, decimal> { ["BTC"] = 0.5m };
            var proposal = new TradeProposal("BTC", 1m, 5000m, TradeSide.Sell);
            var portfolio = CreatePortfolioState(positions: positions, totalEquity: 100_000m);

            var result = riskManager.EvaluateTrade(proposal, portfolio);

            Assert.False(result.IsApproved);
            Assert.Contains("Insufficient position", result.Reason);
        }

        [Fact]
        public void RejectsSellWhenNoPosition()
        {
            var riskManager = CreateRiskManager();
            var proposal = new TradeProposal("BTC", 0.1m, 5000m, TradeSide.Sell);
            var portfolio = CreatePortfolioState(totalEquity: 100_000m);

            var result = riskManager.EvaluateTrade(proposal, portfolio);

            Assert.False(result.IsApproved);
            Assert.Contains("Insufficient position", result.Reason);
        }

        [Fact]
        public void ApprovesValidBuyTrade()
        {
            var riskManager = CreateRiskManager();
            var proposal = new TradeProposal("BTC", 0.1m, 50000m, TradeSide.Buy);
            var portfolio = CreatePortfolioState(totalEquity: 100_000m);

            var result = riskManager.EvaluateTrade(proposal, portfolio);

            Assert.True(result.IsApproved);
            Assert.Contains("all risk checks passed", result.Reason);
        }

        [Fact]
        public void ApprovesValidSellTrade()
        {
            var riskManager = CreateRiskManager();
            var positions = new Dictionary<string, decimal> { ["BTC"] = 1m };
            var proposal = new TradeProposal("BTC", 0.1m, 5000m, TradeSide.Sell);
            var portfolio = CreatePortfolioState(positions: positions, totalEquity: 100_000m);

            var result = riskManager.EvaluateTrade(proposal, portfolio);

            Assert.True(result.IsApproved);
            Assert.Contains("all risk checks passed", result.Reason);
        }

        [Fact]
        public void ApprovesTradeAtExactPositionSizeLimit()
        {
            var riskManager = CreateRiskManager(maxPositionSizePercent: 0.10m);
            var proposal = new TradeProposal("BTC", 0.2m, 50000m, TradeSide.Buy);
            var portfolio = CreatePortfolioState(totalEquity: 100_000m);

            var result = riskManager.EvaluateTrade(proposal, portfolio);

            Assert.True(result.IsApproved);
        }

        [Fact]
        public void SellDoesNotCheckExposureLimit()
        {
            var riskManager = CreateRiskManager(maxExposurePercent: 0.80m, maxPositionSizePercent: 0.50m);
            var positions = new Dictionary<string, decimal> { ["BTC"] = 1m };
            var proposal = new TradeProposal("BTC", 0.5m, 50000m, TradeSide.Sell);
            var portfolio = CreatePortfolioState(
                totalEquity: 100_000m,
                totalExposure: 90_000m,
                positions: positions);

            var result = riskManager.EvaluateTrade(proposal, portfolio);

            Assert.True(result.IsApproved);
        }

        [Fact]
        public void AllowsSellOfEntirePosition()
        {
            var riskManager = CreateRiskManager();
            var positions = new Dictionary<string, decimal> { ["BTC"] = 1m };
            var proposal = new TradeProposal("BTC", 1m, 5000m, TradeSide.Sell);
            var portfolio = CreatePortfolioState(positions: positions, totalEquity: 100_000m);

            var result = riskManager.EvaluateTrade(proposal, portfolio);

            Assert.True(result.IsApproved);
        }
    }

    public class EdgeCaseTests
    {
        [Fact]
        public void HandlesEmptyPortfolio()
        {
            var riskManager = CreateRiskManager();
            var proposal = new TradeProposal("BTC", 0.1m, 50000m, TradeSide.Buy);
            var portfolio = CreatePortfolioState(
                totalEquity: 100_000m,
                peakEquity: 100_000m,
                positions: new Dictionary<string, decimal>(),
                totalExposure: 0m);

            var result = riskManager.EvaluateTrade(proposal, portfolio);

            Assert.True(result.IsApproved);
        }

        [Fact]
        public void HandlesZeroTotalEquity_ForBuy()
        {
            var riskManager = CreateRiskManager();
            var proposal = new TradeProposal("BTC", 0.001m, 100m, TradeSide.Buy);
            var portfolio = CreatePortfolioState(totalEquity: 0m);

            var result = riskManager.EvaluateTrade(proposal, portfolio);

            Assert.False(result.IsApproved);
        }

        [Fact]
        public void HandlesVerySmallTradeValue()
        {
            var riskManager = CreateRiskManager(minOrderValue: 0.01m);
            var proposal = new TradeProposal("BTC", 0.0001m, 500m, TradeSide.Buy);
            var portfolio = CreatePortfolioState(totalEquity: 100_000m);

            var result = riskManager.EvaluateTrade(proposal, portfolio);

            Assert.True(result.IsApproved);
        }

        [Fact]
        public void HandlesVeryLargeTradeValue()
        {
            var riskManager = CreateRiskManager(maxPositionSizePercent: 0.50m);
            var proposal = new TradeProposal("BTC", 100m, 500000m, TradeSide.Buy);
            var portfolio = CreatePortfolioState(totalEquity: 100_000_000m);

            var result = riskManager.EvaluateTrade(proposal, portfolio);

            Assert.True(result.IsApproved);
        }

        [Fact]
        public void HandlesMultipleAssetPositions()
        {
            var riskManager = CreateRiskManager();
            var positions = new Dictionary<string, decimal>
            {
                ["BTC"] = 1m,
                ["ETH"] = 10m,
                ["SPY"] = 100m
            };
            var proposal = new TradeProposal("ETH", 2m, 3000m, TradeSide.Sell);
            var portfolio = CreatePortfolioState(positions: positions, totalEquity: 100_000m);

            var result = riskManager.EvaluateTrade(proposal, portfolio);

            Assert.True(result.IsApproved);
        }

        [Fact]
        public void HandlesNewAssetNotInPortfolio_ForBuy()
        {
            var riskManager = CreateRiskManager();
            var positions = new Dictionary<string, decimal> { ["BTC"] = 1m };
            var proposal = new TradeProposal("ETH", 0.1m, 3000m, TradeSide.Buy);
            var portfolio = CreatePortfolioState(
                totalEquity: 100_000m,
                positions: positions);

            var result = riskManager.EvaluateTrade(proposal, portfolio);

            Assert.True(result.IsApproved);
        }

        [Fact]
        public void HandlesNewAssetNotInPortfolio_ForSell()
        {
            var riskManager = CreateRiskManager();
            var positions = new Dictionary<string, decimal> { ["BTC"] = 1m };
            var proposal = new TradeProposal("ETH", 0.1m, 3000m, TradeSide.Sell);
            var portfolio = CreatePortfolioState(positions: positions);

            var result = riskManager.EvaluateTrade(proposal, portfolio);

            Assert.False(result.IsApproved);
            Assert.Contains("Insufficient position", result.Reason);
        }

        [Fact]
        public void HandlesDecimalPrecision()
        {
            var riskManager = CreateRiskManager(maxPositionSizePercent: 0.10m, minOrderValue: 1m);
            var proposal = new TradeProposal("BTC", 0.00012345m, 67891.23456m, TradeSide.Buy);
            var portfolio = CreatePortfolioState(totalEquity: 100_000m);

            var result = riskManager.EvaluateTrade(proposal, portfolio);

            Assert.True(result.IsApproved);
        }
    }

    public class GetCurrentLimitsTests
    {
        [Fact]
        public void ReturnsConfiguredLimits()
        {
            var riskManager = CreateRiskManager(
                maxDrawdownPercent: 0.05m,
                maxPositionSizePercent: 0.10m,
                maxExposurePercent: 0.80m,
                minOrderValue: 10m);

            var limits = riskManager.GetCurrentLimits();

            Assert.Equal(0.05m, limits.MaxDrawdownPercent);
            Assert.Equal(0.10m, limits.MaxPositionSizePercent);
            Assert.Equal(0.80m, limits.MaxExposurePercent);
            Assert.Equal(10m, limits.MinOrderValue);
        }
    }

    public class UpdateLimitsTests
    {
        [Fact]
        public void UpdatesMaxDrawdownPercent()
        {
            var riskManager = CreateRiskManager();
            var request = new RiskLimitsUpdateRequest(
                MaxDrawdownPercent: 0.10m,
                MaxPositionSizePercent: null,
                MaxExposurePercent: null,
                MinOrderValue: null);

            var result = riskManager.UpdateLimits(request);

            Assert.Equal(0.10m, result.MaxDrawdownPercent);
            Assert.Equal(0.10m, riskManager.GetCurrentLimits().MaxDrawdownPercent);
        }

        [Fact]
        public void UpdatesMaxPositionSizePercent()
        {
            var riskManager = CreateRiskManager();
            var request = new RiskLimitsUpdateRequest(
                MaxDrawdownPercent: null,
                MaxPositionSizePercent: 0.20m,
                MaxExposurePercent: null,
                MinOrderValue: null);

            var result = riskManager.UpdateLimits(request);

            Assert.Equal(0.20m, result.MaxPositionSizePercent);
        }

        [Fact]
        public void UpdatesMaxExposurePercent()
        {
            var riskManager = CreateRiskManager();
            var request = new RiskLimitsUpdateRequest(
                MaxDrawdownPercent: null,
                MaxPositionSizePercent: null,
                MaxExposurePercent: 0.90m,
                MinOrderValue: null);

            var result = riskManager.UpdateLimits(request);

            Assert.Equal(0.90m, result.MaxExposurePercent);
        }

        [Fact]
        public void UpdatesMinOrderValue()
        {
            var riskManager = CreateRiskManager();
            var request = new RiskLimitsUpdateRequest(
                MaxDrawdownPercent: null,
                MaxPositionSizePercent: null,
                MaxExposurePercent: null,
                MinOrderValue: 50m);

            var result = riskManager.UpdateLimits(request);

            Assert.Equal(50m, result.MinOrderValue);
        }

        [Fact]
        public void UpdatesMultipleLimits()
        {
            var riskManager = CreateRiskManager();
            var request = new RiskLimitsUpdateRequest(
                MaxDrawdownPercent: 0.03m,
                MaxPositionSizePercent: 0.15m,
                MaxExposurePercent: 0.70m,
                MinOrderValue: 25m);

            var result = riskManager.UpdateLimits(request);

            Assert.Equal(0.03m, result.MaxDrawdownPercent);
            Assert.Equal(0.15m, result.MaxPositionSizePercent);
            Assert.Equal(0.70m, result.MaxExposurePercent);
            Assert.Equal(25m, result.MinOrderValue);
        }

        [Fact]
        public void PreservesUnchangedLimits()
        {
            var riskManager = CreateRiskManager(
                maxDrawdownPercent: 0.05m,
                maxPositionSizePercent: 0.10m,
                maxExposurePercent: 0.80m,
                minOrderValue: 10m);

            var request = new RiskLimitsUpdateRequest(
                MaxDrawdownPercent: 0.07m,
                MaxPositionSizePercent: null,
                MaxExposurePercent: null,
                MinOrderValue: null);

            var result = riskManager.UpdateLimits(request);

            Assert.Equal(0.07m, result.MaxDrawdownPercent);
            Assert.Equal(0.10m, result.MaxPositionSizePercent);
            Assert.Equal(0.80m, result.MaxExposurePercent);
            Assert.Equal(10m, result.MinOrderValue);
        }

        [Fact]
        public void UpdatedLimitsAffectFutureTradeEvaluation()
        {
            var riskManager = CreateRiskManager(maxPositionSizePercent: 0.10m);
            var proposal = new TradeProposal("BTC", 0.3m, 50000m, TradeSide.Buy);
            var portfolio = CreatePortfolioState(totalEquity: 100_000m);

            var beforeUpdate = riskManager.EvaluateTrade(proposal, portfolio);
            Assert.False(beforeUpdate.IsApproved);

            riskManager.UpdateLimits(new RiskLimitsUpdateRequest(
                MaxDrawdownPercent: null,
                MaxPositionSizePercent: 0.20m,
                MaxExposurePercent: null,
                MinOrderValue: null));

            var afterUpdate = riskManager.EvaluateTrade(proposal, portfolio);
            Assert.True(afterUpdate.IsApproved);
        }
    }

    public class ThreadSafetyTests
    {
        [Fact]
        public void ConcurrentReadsReturnConsistentLimits()
        {
            var riskManager = CreateRiskManager();
            var results = new List<RiskLimits>();
            var exceptions = new List<Exception>();

            Parallel.For(0, 100, i =>
            {
                try
                {
                    var limits = riskManager.GetCurrentLimits();
                    lock (results)
                    {
                        results.Add(limits);
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            });

            Assert.Empty(exceptions);
            Assert.All(results, r => Assert.Equal(0.05m, r.MaxDrawdownPercent));
        }

        [Fact]
        public void ConcurrentUpdatesDoNotCorruptState()
        {
            var riskManager = CreateRiskManager();
            var exceptions = new List<Exception>();

            Parallel.For(0, 50, i =>
            {
                try
                {
                    var newValue = 0.01m + (i * 0.001m);
                    riskManager.UpdateLimits(new RiskLimitsUpdateRequest(
                        MaxDrawdownPercent: newValue,
                        MaxPositionSizePercent: null,
                        MaxExposurePercent: null,
                        MinOrderValue: null));
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            });

            Assert.Empty(exceptions);
            var finalLimits = riskManager.GetCurrentLimits();
            Assert.True(finalLimits.MaxDrawdownPercent >= 0.01m && finalLimits.MaxDrawdownPercent <= 0.06m);
        }

        [Fact]
        public void ConcurrentEvaluationsDoNotThrow()
        {
            var riskManager = CreateRiskManager();
            var portfolio = CreatePortfolioState(totalEquity: 100_000m);
            var exceptions = new List<Exception>();

            Parallel.For(0, 100, i =>
            {
                try
                {
                    var proposal = new TradeProposal("BTC", 0.1m, 50000m, TradeSide.Buy);
                    riskManager.EvaluateTrade(proposal, portfolio);
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            });

            Assert.Empty(exceptions);
        }
    }
}

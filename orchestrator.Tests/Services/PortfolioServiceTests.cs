using Microsoft.Extensions.Options;
using QuantFlow.Orchestrator.Configuration;
using QuantFlow.Orchestrator.Services;

namespace QuantFlow.Orchestrator.Tests.Services;

public class PortfolioServiceTests
{
    private static PortfolioService CreatePortfolioService(decimal initialCash = 100_000m)
    {
        var settings = new PortfolioSettings { InitialCash = initialCash };
        return new PortfolioService(Options.Create(settings));
    }

    private static ExecutedTrade CreateTrade(
        string asset,
        decimal quantity,
        decimal price,
        TradeSide side,
        DateTime? executedAt = null)
    {
        return new ExecutedTrade(asset, quantity, price, side, executedAt ?? DateTime.UtcNow);
    }

    public class InitialStateTests
    {
        [Fact]
        public void CashBalance_EqualsInitialCash()
        {
            var service = CreatePortfolioService(50_000m);
            Assert.Equal(50_000m, service.CashBalance);
        }

        [Fact]
        public void RealizedPnL_IsZero()
        {
            var service = CreatePortfolioService();
            Assert.Equal(0m, service.RealizedPnL);
        }

        [Fact]
        public void PeakEquity_EqualsInitialCash()
        {
            var service = CreatePortfolioService(75_000m);
            Assert.Equal(75_000m, service.PeakEquity);
        }

        [Fact]
        public void GetAllPositions_ReturnsEmptyDictionary()
        {
            var service = CreatePortfolioService();
            var positions = service.GetAllPositions();
            Assert.Empty(positions);
        }

        [Fact]
        public void GetPortfolioValue_EqualsInitialCash()
        {
            var service = CreatePortfolioService(100_000m);
            Assert.Equal(100_000m, service.GetPortfolioValue());
        }

        [Fact]
        public void GetPosition_ReturnsNull_ForNonexistentAsset()
        {
            var service = CreatePortfolioService();
            var position = service.GetPosition("BTC");
            Assert.Null(position);
        }

        [Fact]
        public void GetSummary_ReturnsCorrectInitialState()
        {
            var service = CreatePortfolioService(100_000m);
            var summary = service.GetSummary();

            Assert.Equal(100_000m, summary.CashBalance);
            Assert.Equal(0m, summary.TotalMarketValue);
            Assert.Equal(100_000m, summary.TotalEquity);
            Assert.Equal(0m, summary.RealizedPnL);
            Assert.Equal(0m, summary.UnrealizedPnL);
            Assert.Equal(100_000m, summary.PeakEquity);
            Assert.Empty(summary.Positions);
        }
    }

    public class BuyPositionTests
    {
        [Fact]
        public void OpeningBuy_DeductsCashBalance()
        {
            var service = CreatePortfolioService(100_000m);
            var trade = CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy);

            service.UpdatePositionAfterTrade(trade);

            Assert.Equal(50_000m, service.CashBalance);
        }

        [Fact]
        public void OpeningBuy_CreatesPosition()
        {
            var service = CreatePortfolioService(100_000m);
            var trade = CreateTrade("BTC", 2m, 30_000m, TradeSide.Buy);

            service.UpdatePositionAfterTrade(trade);

            var position = service.GetPosition("BTC");
            Assert.NotNull(position);
            Assert.Equal("BTC", position.Asset);
            Assert.Equal(2m, position.Quantity);
            Assert.Equal(30_000m, position.AverageCost);
            Assert.Equal(30_000m, position.CurrentPrice);
        }

        [Fact]
        public void OpeningBuy_PortfolioValueRemainsSame()
        {
            var service = CreatePortfolioService(100_000m);
            var trade = CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy);

            service.UpdatePositionAfterTrade(trade);

            Assert.Equal(100_000m, service.GetPortfolioValue());
        }

        [Fact]
        public void AdditionalBuy_AveragesCost()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 40_000m, TradeSide.Buy));
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 60_000m, TradeSide.Buy));

            var position = service.GetPosition("BTC");
            Assert.NotNull(position);
            Assert.Equal(2m, position.Quantity);
            Assert.Equal(50_000m, position.AverageCost);
        }

        [Fact]
        public void AdditionalBuy_CorrectlyWeightsAverageCost()
        {
            var service = CreatePortfolioService(200_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 2m, 40_000m, TradeSide.Buy));
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 70_000m, TradeSide.Buy));

            var position = service.GetPosition("BTC");
            Assert.NotNull(position);
            Assert.Equal(3m, position.Quantity);
            Assert.Equal(50_000m, position.AverageCost);
        }

        [Fact]
        public void MultipleBuys_DeductsCashCorrectly()
        {
            var service = CreatePortfolioService(200_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy));
            service.UpdatePositionAfterTrade(CreateTrade("ETH", 10m, 3_000m, TradeSide.Buy));

            Assert.Equal(120_000m, service.CashBalance);
        }

        [Fact]
        public void BuyingMultipleAssets_CreatesMultiplePositions()
        {
            var service = CreatePortfolioService(200_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy));
            service.UpdatePositionAfterTrade(CreateTrade("ETH", 10m, 3_000m, TradeSide.Buy));

            var positions = service.GetAllPositions();
            Assert.Equal(2, positions.Count);
            Assert.Contains("BTC", positions.Keys);
            Assert.Contains("ETH", positions.Keys);
        }
    }

    public class SellPositionTests
    {
        [Fact]
        public void SellingPosition_AddsCashBalance()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy));
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 55_000m, TradeSide.Sell));

            Assert.Equal(105_000m, service.CashBalance);
        }

        [Fact]
        public void SellingPartialPosition_ReducesQuantity()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 2m, 50_000m, TradeSide.Buy));
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 55_000m, TradeSide.Sell));

            var position = service.GetPosition("BTC");
            Assert.NotNull(position);
            Assert.Equal(1m, position.Quantity);
        }

        [Fact]
        public void SellingEntirePosition_RemovesPosition()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy));
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 55_000m, TradeSide.Sell));

            var position = service.GetPosition("BTC");
            Assert.Null(position);
        }

        [Fact]
        public void SellingPartialPosition_PreservesAverageCost()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 2m, 40_000m, TradeSide.Buy));
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Sell));

            var position = service.GetPosition("BTC");
            Assert.NotNull(position);
            Assert.Equal(40_000m, position.AverageCost);
        }

        [Fact]
        public void SellingNonexistentPosition_DoesNothing()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Sell));

            Assert.Equal(150_000m, service.CashBalance);
            Assert.Empty(service.GetAllPositions());
        }
    }

    public class PnLCalculationTests
    {
        [Fact]
        public void RealizedPnL_PositiveWhenSellingHigher()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy));
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 55_000m, TradeSide.Sell));

            Assert.Equal(5_000m, service.RealizedPnL);
        }

        [Fact]
        public void RealizedPnL_NegativeWhenSellingLower()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy));
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 45_000m, TradeSide.Sell));

            Assert.Equal(-5_000m, service.RealizedPnL);
        }

        [Fact]
        public void RealizedPnL_ZeroWhenSellingAtCost()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy));
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Sell));

            Assert.Equal(0m, service.RealizedPnL);
        }

        [Fact]
        public void RealizedPnL_AccumulatesAcrossMultipleTrades()
        {
            var service = CreatePortfolioService(200_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy));
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 55_000m, TradeSide.Sell));
            service.UpdatePositionAfterTrade(CreateTrade("ETH", 10m, 3_000m, TradeSide.Buy));
            service.UpdatePositionAfterTrade(CreateTrade("ETH", 10m, 3_200m, TradeSide.Sell));

            Assert.Equal(7_000m, service.RealizedPnL);
        }

        [Fact]
        public void UnrealizedPnL_ReflectsPriceChange()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy));
            service.UpdatePrice("BTC", 55_000m);

            var summary = service.GetSummary();
            Assert.Equal(5_000m, summary.UnrealizedPnL);
        }

        [Fact]
        public void UnrealizedPnL_NegativeWhenPriceDrops()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy));
            service.UpdatePrice("BTC", 45_000m);

            var summary = service.GetSummary();
            Assert.Equal(-5_000m, summary.UnrealizedPnL);
        }

        [Fact]
        public void PartialSell_CalculatesRealizedPnLCorrectly()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 2m, 50_000m, TradeSide.Buy));
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 60_000m, TradeSide.Sell));

            Assert.Equal(10_000m, service.RealizedPnL);
        }
    }

    public class PriceUpdateTests
    {
        [Fact]
        public void UpdatePrice_ChangesPositionCurrentPrice()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy));
            service.UpdatePrice("BTC", 60_000m);

            var position = service.GetPosition("BTC");
            Assert.NotNull(position);
            Assert.Equal(60_000m, position.CurrentPrice);
        }

        [Fact]
        public void UpdatePrice_AffectsPortfolioValue()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy));
            service.UpdatePrice("BTC", 60_000m);

            Assert.Equal(110_000m, service.GetPortfolioValue());
        }

        [Fact]
        public void UpdatePrice_UpdatesPeakEquityWhenValueIncreases()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy));
            service.UpdatePrice("BTC", 70_000m);

            Assert.Equal(120_000m, service.PeakEquity);
        }

        [Fact]
        public void UpdatePrice_DoesNotLowerPeakEquity()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy));
            service.UpdatePrice("BTC", 70_000m);
            service.UpdatePrice("BTC", 40_000m);

            Assert.Equal(120_000m, service.PeakEquity);
        }

        [Fact]
        public void UpdatePrice_ForNonexistentAsset_DoesNothing()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePrice("BTC", 60_000m);

            Assert.Equal(100_000m, service.GetPortfolioValue());
            Assert.Equal(100_000m, service.PeakEquity);
        }

        [Fact]
        public void UpdatePrice_PreservesAverageCost()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy));
            service.UpdatePrice("BTC", 60_000m);

            var position = service.GetPosition("BTC");
            Assert.NotNull(position);
            Assert.Equal(50_000m, position.AverageCost);
        }
    }

    public class PortfolioStateForRiskTests
    {
        [Fact]
        public void ReturnsCorrectTotalEquity()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy));
            service.UpdatePrice("BTC", 60_000m);

            var state = service.GetPortfolioStateForRisk();
            Assert.Equal(110_000m, state.TotalEquity);
        }

        [Fact]
        public void ReturnsCorrectPeakEquity()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy));
            service.UpdatePrice("BTC", 70_000m);
            service.UpdatePrice("BTC", 60_000m);

            var state = service.GetPortfolioStateForRisk();
            Assert.Equal(120_000m, state.PeakEquity);
        }

        [Fact]
        public void ReturnsCorrectPositionQuantities()
        {
            var service = CreatePortfolioService(200_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 2m, 50_000m, TradeSide.Buy));
            service.UpdatePositionAfterTrade(CreateTrade("ETH", 10m, 3_000m, TradeSide.Buy));

            var state = service.GetPortfolioStateForRisk();
            Assert.Equal(2m, state.Positions["BTC"]);
            Assert.Equal(10m, state.Positions["ETH"]);
        }

        [Fact]
        public void ReturnsCorrectTotalExposure()
        {
            var service = CreatePortfolioService(200_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy));
            service.UpdatePositionAfterTrade(CreateTrade("ETH", 10m, 3_000m, TradeSide.Buy));

            var state = service.GetPortfolioStateForRisk();
            Assert.Equal(80_000m, state.TotalExposure);
        }
    }

    public class ResetTests
    {
        [Fact]
        public void Reset_ClearsAllPositions()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy));
            service.Reset(100_000m);

            Assert.Empty(service.GetAllPositions());
        }

        [Fact]
        public void Reset_SetsCashToNewInitialAmount()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy));
            service.Reset(200_000m);

            Assert.Equal(200_000m, service.CashBalance);
        }

        [Fact]
        public void Reset_ClearsRealizedPnL()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy));
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 55_000m, TradeSide.Sell));
            service.Reset(100_000m);

            Assert.Equal(0m, service.RealizedPnL);
        }

        [Fact]
        public void Reset_SetsPeakEquityToNewInitialAmount()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy));
            service.UpdatePrice("BTC", 70_000m);
            service.Reset(150_000m);

            Assert.Equal(150_000m, service.PeakEquity);
        }

        [Fact]
        public void Reset_PortfolioValueEqualsNewInitialCash()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy));
            service.Reset(75_000m);

            Assert.Equal(75_000m, service.GetPortfolioValue());
        }
    }

    public class LongShortPositionTests
    {
        [Fact]
        public void MultipleSmallBuys_BuildLongPosition()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 0.5m, 50_000m, TradeSide.Buy));
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 0.3m, 52_000m, TradeSide.Buy));
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 0.2m, 48_000m, TradeSide.Buy));

            var position = service.GetPosition("BTC");
            Assert.NotNull(position);
            Assert.Equal(1m, position.Quantity);
            var expectedAvgCost = (0.5m * 50_000m + 0.3m * 52_000m + 0.2m * 48_000m) / 1m;
            Assert.Equal(expectedAvgCost, position.AverageCost);
        }

        [Fact]
        public void ClosingLongPosition_CalculatesCorrectPnL()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy));
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 60_000m, TradeSide.Sell));

            Assert.Null(service.GetPosition("BTC"));
            Assert.Equal(10_000m, service.RealizedPnL);
            Assert.Equal(110_000m, service.CashBalance);
        }

        [Fact]
        public void PartialClose_LeavesRemainingPosition()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 2m, 50_000m, TradeSide.Buy));
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 0.5m, 55_000m, TradeSide.Sell));

            var position = service.GetPosition("BTC");
            Assert.NotNull(position);
            Assert.Equal(1.5m, position.Quantity);
            Assert.Equal(50_000m, position.AverageCost);
        }

        [Fact]
        public void PartialCloseWithLoss_CalculatesCorrectPnL()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 2m, 50_000m, TradeSide.Buy));
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 45_000m, TradeSide.Sell));

            Assert.Equal(-5_000m, service.RealizedPnL);
        }

        [Fact]
        public void SellingMoreThanOwned_OnlyDeductsOwnedQuantity()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy));
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 2m, 55_000m, TradeSide.Sell));

            var position = service.GetPosition("BTC");
            Assert.Null(position);
        }
    }

    public class ConcurrentAccessTests
    {
        [Fact]
        public void ConcurrentReads_DoNotThrow()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy));
            var exceptions = new List<Exception>();

            Parallel.For(0, 100, iteration =>
            {
                try
                {
                    var cashBalance = service.CashBalance;
                    var realizedPnL = service.RealizedPnL;
                    var peakEquity = service.PeakEquity;
                    var portfolioValue = service.GetPortfolioValue();
                    var position = service.GetPosition("BTC");
                    var allPositions = service.GetAllPositions();
                    var stateForRisk = service.GetPortfolioStateForRisk();
                    var summary = service.GetSummary();
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

        [Fact]
        public void ConcurrentBuys_DoNotCorruptState()
        {
            var service = CreatePortfolioService(10_000_000m);
            var exceptions = new List<Exception>();

            Parallel.For(0, 100, i =>
            {
                try
                {
                    var asset = $"ASSET{i % 10}";
                    service.UpdatePositionAfterTrade(CreateTrade(asset, 0.1m, 100m, TradeSide.Buy));
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
            var totalPositions = service.GetAllPositions().Values.Sum(p => p.Quantity);
            Assert.Equal(10m, totalPositions);
        }

        [Fact]
        public void ConcurrentPriceUpdates_DoNotThrow()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy));
            var exceptions = new List<Exception>();

            Parallel.For(0, 100, i =>
            {
                try
                {
                    service.UpdatePrice("BTC", 50_000m + i * 100m);
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

        [Fact]
        public void ConcurrentMixedOperations_DoNotCorruptState()
        {
            var service = CreatePortfolioService(10_000_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 100m, 1000m, TradeSide.Buy));
            var exceptions = new List<Exception>();

            Parallel.Invoke(
                () =>
                {
                    for (int i = 0; i < 50; i++)
                    {
                        try
                        {
                            service.UpdatePositionAfterTrade(CreateTrade("ETH", 0.1m, 100m, TradeSide.Buy));
                        }
                        catch (Exception ex)
                        {
                            lock (exceptions) { exceptions.Add(ex); }
                        }
                    }
                },
                () =>
                {
                    for (int i = 0; i < 50; i++)
                    {
                        try
                        {
                            service.UpdatePrice("BTC", 1000m + i);
                        }
                        catch (Exception ex)
                        {
                            lock (exceptions) { exceptions.Add(ex); }
                        }
                    }
                },
                () =>
                {
                    for (int i = 0; i < 50; i++)
                    {
                        try
                        {
                            _ = service.GetSummary();
                            _ = service.GetPortfolioStateForRisk();
                        }
                        catch (Exception ex)
                        {
                            lock (exceptions) { exceptions.Add(ex); }
                        }
                    }
                }
            );

            Assert.Empty(exceptions);
        }

        [Fact]
        public void ConcurrentReadsAndWrites_ReturnConsistentPositionData()
        {
            var service = CreatePortfolioService(100_000m);
            var exceptions = new List<Exception>();

            Parallel.For(0, 100, i =>
            {
                try
                {
                    if (i % 2 == 0)
                    {
                        service.UpdatePositionAfterTrade(CreateTrade("BTC", 0.01m, 1000m, TradeSide.Buy));
                    }
                    else
                    {
                        var position = service.GetPosition("BTC");
                        if (position != null)
                        {
                            Assert.True(position.Quantity >= 0);
                            Assert.True(position.AverageCost > 0);
                        }
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
        }
    }

    public class PositionRecordTests
    {
        [Fact]
        public void Position_MarketValue_IsQuantityTimesCurrentPrice()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 2m, 50_000m, TradeSide.Buy));
            service.UpdatePrice("BTC", 55_000m);

            var position = service.GetPosition("BTC");
            Assert.NotNull(position);
            Assert.Equal(110_000m, position.MarketValue);
        }

        [Fact]
        public void Position_CostBasis_IsQuantityTimesAverageCost()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 2m, 50_000m, TradeSide.Buy));

            var position = service.GetPosition("BTC");
            Assert.NotNull(position);
            Assert.Equal(100_000m, position.CostBasis);
        }

        [Fact]
        public void Position_UnrealizedPnL_IsMarketValueMinusCostBasis()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 2m, 50_000m, TradeSide.Buy));
            service.UpdatePrice("BTC", 55_000m);

            var position = service.GetPosition("BTC");
            Assert.NotNull(position);
            Assert.Equal(10_000m, position.UnrealizedPnL);
        }

        [Fact]
        public void Position_UnrealizedPnLPercent_CalculatesCorrectly()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 2m, 50_000m, TradeSide.Buy));
            service.UpdatePrice("BTC", 55_000m);

            var position = service.GetPosition("BTC");
            Assert.NotNull(position);
            Assert.Equal(10m, position.UnrealizedPnLPercent);
        }
    }

    public class EdgeCaseTests
    {
        [Fact]
        public void ZeroInitialCash_WorksCorrectly()
        {
            var service = CreatePortfolioService(0m);
            Assert.Equal(0m, service.CashBalance);
            Assert.Equal(0m, service.GetPortfolioValue());
        }

        [Fact]
        public void VerySmallQuantity_HandledCorrectly()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 0.00000001m, 50_000m, TradeSide.Buy));

            var position = service.GetPosition("BTC");
            Assert.NotNull(position);
            Assert.Equal(0.00000001m, position.Quantity);
        }

        [Fact]
        public void VeryLargeQuantity_HandledCorrectly()
        {
            var service = CreatePortfolioService(1_000_000_000_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1_000_000m, 50_000m, TradeSide.Buy));

            var position = service.GetPosition("BTC");
            Assert.NotNull(position);
            Assert.Equal(1_000_000m, position.Quantity);
        }

        [Fact]
        public void GetAllPositions_ReturnsDefensiveCopy()
        {
            var service = CreatePortfolioService(100_000m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 1m, 50_000m, TradeSide.Buy));

            var positions = service.GetAllPositions();
            service.UpdatePositionAfterTrade(CreateTrade("ETH", 10m, 3_000m, TradeSide.Buy));

            Assert.Single(positions);
            Assert.Equal(2, service.GetAllPositions().Count);
        }

        [Fact]
        public void DecimalPrecision_MaintainedInCalculations()
        {
            var service = CreatePortfolioService(100_000.123456789m);
            service.UpdatePositionAfterTrade(CreateTrade("BTC", 0.123456789m, 50_000.987654321m, TradeSide.Buy));

            var position = service.GetPosition("BTC");
            Assert.NotNull(position);
            Assert.Equal(0.123456789m, position.Quantity);
        }
    }
}

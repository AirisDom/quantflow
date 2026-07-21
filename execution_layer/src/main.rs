pub mod quantflow {
    tonic::include_proto!("quantflow");
}

use quantflow::execution_service_server::{ExecutionService, ExecutionServiceServer};
use quantflow::{ExecutionReceipt, ExecutionStatus, OrderRequest, OrderSide};
use rand::Rng;
use std::env;
use std::time::{SystemTime, UNIX_EPOCH};
use tonic::{transport::Server, Request, Response, Status};
use tracing::{info, warn, instrument};
use tracing_subscriber::{fmt, prelude::*, EnvFilter};
use uuid::Uuid;

pub struct ExecutionServiceImpl {
    min_latency_ms: u64,
    max_latency_ms: u64,
}

impl ExecutionServiceImpl {
    pub fn new(min_latency_ms: u64, max_latency_ms: u64) -> Self {
        Self {
            min_latency_ms,
            max_latency_ms,
        }
    }
}

#[tonic::async_trait]
impl ExecutionService for ExecutionServiceImpl {
    #[instrument(
        skip(self, request),
        fields(
            service = "QuantFlow.ExecutionLayer",
            asset = %request.get_ref().asset,
            quantity = %request.get_ref().quantity
        )
    )]
    async fn execute_order(
        &self,
        request: Request<OrderRequest>,
    ) -> Result<Response<ExecutionReceipt>, Status> {
        let correlation_id = request
            .metadata()
            .get("x-correlation-id")
            .and_then(|v| v.to_str().ok())
            .map(|s| s.to_string());

        let order = request.into_inner();

        let side_str = match order.side() {
            OrderSide::SideBuy => "BUY",
            OrderSide::SideSell => "SELL",
        };

        info!(
            asset = %order.asset,
            quantity = %order.quantity,
            side = %side_str,
            correlation_id = ?correlation_id,
            "Received order"
        );

        if order.asset.is_empty() {
            warn!(correlation_id = ?correlation_id, "Order rejected: asset cannot be empty");
            return Err(Status::invalid_argument("asset cannot be empty"));
        }

        if order.quantity <= 0.0 {
            warn!(
                asset = %order.asset,
                quantity = %order.quantity,
                correlation_id = ?correlation_id,
                "Order rejected: quantity must be positive"
            );
            return Err(Status::invalid_argument("quantity must be positive"));
        }

        let (delay_ms, slippage) = {
            let mut rng = rand::thread_rng();
            let delay = rng.gen_range(self.min_latency_ms..=self.max_latency_ms);
            let slip = rng.gen_range(-0.001..=0.001);
            (delay, slip)
        };

        tokio::time::sleep(tokio::time::Duration::from_millis(delay_ms)).await;

        let base_price = get_mock_market_price(&order.asset);
        let fill_price = calculate_fill_price(base_price, slippage);

        let timestamp = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_millis() as i64;

        let order_id = Uuid::new_v4().to_string();

        let receipt = ExecutionReceipt {
            order_id: order_id.clone(),
            fill_price,
            status: ExecutionStatus::Filled.into(),
            timestamp,
        };

        info!(
            order_id = %order_id,
            asset = %order.asset,
            side = %side_str,
            fill_price = %format!("{:.4}", fill_price),
            slippage_pct = %format!("{:+.4}", slippage * 100.0),
            latency_ms = %delay_ms,
            correlation_id = ?correlation_id,
            "Order executed"
        );

        Ok(Response::new(receipt))
    }
}

pub fn get_mock_market_price(asset: &str) -> f64 {
    match asset.to_uppercase().as_str() {
        "BTC" | "BTCUSD" => 45000.0,
        "ETH" | "ETHUSD" => 2500.0,
        "AAPL" => 175.0,
        "GOOGL" => 140.0,
        "MSFT" => 380.0,
        "SPY" => 450.0,
        _ => 100.0,
    }
}

pub fn calculate_fill_price(base_price: f64, slippage: f64) -> f64 {
    base_price * (1.0 + slippage)
}

fn init_tracing() {
    let log_level = env::var("RUST_LOG")
        .unwrap_or_else(|_| "info".to_string());

    let filter = EnvFilter::try_from_default_env()
        .unwrap_or_else(|_| EnvFilter::new(&log_level));

    tracing_subscriber::registry()
        .with(filter)
        .with(
            fmt::layer()
                .json()
                .with_target(true)
                .with_thread_ids(true)
                .with_file(false)
                .with_line_number(false)
                .flatten_event(true)
                .with_current_span(true)
        )
        .init();
}

#[tokio::main]
async fn main() -> Result<(), Box<dyn std::error::Error>> {
    init_tracing();

    let port = env::var("EXECUTION_SERVICE_PORT").unwrap_or_else(|_| "50052".to_string());
    let min_latency_ms: u64 = env::var("EXECUTION_MIN_LATENCY_MS")
        .unwrap_or_else(|_| "10".to_string())
        .parse()
        .unwrap_or(10);
    let max_latency_ms: u64 = env::var("EXECUTION_MAX_LATENCY_MS")
        .unwrap_or_else(|_| "50".to_string())
        .parse()
        .unwrap_or(50);

    let addr = format!("[::]:{}", port).parse()?;
    let service = ExecutionServiceImpl::new(min_latency_ms, max_latency_ms);

    info!(
        service = "QuantFlow.ExecutionLayer",
        version = "1.0.0",
        port = %port,
        min_latency_ms = %min_latency_ms,
        max_latency_ms = %max_latency_ms,
        "Starting gRPC server"
    );

    Server::builder()
        .add_service(ExecutionServiceServer::new(service))
        .serve(addr)
        .await?;

    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::HashSet;
    use std::time::Instant;
    use tonic::Request;

    fn create_test_request(asset: &str, quantity: f64, side: OrderSide) -> Request<OrderRequest> {
        Request::new(OrderRequest {
            asset: asset.to_string(),
            quantity,
            side: side.into(),
        })
    }

    #[tokio::test]
    async fn test_execute_order_returns_valid_receipt() {
        let service = ExecutionServiceImpl::new(1, 5);
        let request = create_test_request("BTC", 1.0, OrderSide::SideBuy);

        let response = service.execute_order(request).await;

        assert!(response.is_ok());
        let receipt = response.unwrap().into_inner();
        assert!(!receipt.order_id.is_empty());
        assert!(receipt.fill_price > 0.0);
        assert_eq!(receipt.status, ExecutionStatus::Filled as i32);
        assert!(receipt.timestamp > 0);
    }

    #[tokio::test]
    async fn test_execute_order_rejects_empty_asset() {
        let service = ExecutionServiceImpl::new(1, 5);
        let request = create_test_request("", 1.0, OrderSide::SideBuy);

        let response = service.execute_order(request).await;

        assert!(response.is_err());
        let status = response.unwrap_err();
        assert_eq!(status.code(), tonic::Code::InvalidArgument);
        assert!(status.message().contains("asset cannot be empty"));
    }

    #[tokio::test]
    async fn test_execute_order_rejects_zero_quantity() {
        let service = ExecutionServiceImpl::new(1, 5);
        let request = create_test_request("BTC", 0.0, OrderSide::SideBuy);

        let response = service.execute_order(request).await;

        assert!(response.is_err());
        let status = response.unwrap_err();
        assert_eq!(status.code(), tonic::Code::InvalidArgument);
        assert!(status.message().contains("quantity must be positive"));
    }

    #[tokio::test]
    async fn test_execute_order_rejects_negative_quantity() {
        let service = ExecutionServiceImpl::new(1, 5);
        let request = create_test_request("BTC", -10.0, OrderSide::SideSell);

        let response = service.execute_order(request).await;

        assert!(response.is_err());
        let status = response.unwrap_err();
        assert_eq!(status.code(), tonic::Code::InvalidArgument);
    }

    #[tokio::test]
    async fn test_latency_simulation_within_bounds() {
        let min_latency = 10u64;
        let max_latency = 50u64;
        let service = ExecutionServiceImpl::new(min_latency, max_latency);

        for _ in 0..5 {
            let request = create_test_request("ETH", 2.0, OrderSide::SideBuy);
            let start = Instant::now();

            let _ = service.execute_order(request).await;

            let elapsed = start.elapsed().as_millis() as u64;
            assert!(
                elapsed >= min_latency,
                "Elapsed {}ms should be >= min {}ms",
                elapsed,
                min_latency
            );
            assert!(
                elapsed <= max_latency + 20,
                "Elapsed {}ms should be <= max {}ms + tolerance",
                elapsed,
                max_latency
            );
        }
    }

    #[tokio::test]
    async fn test_latency_with_narrow_range() {
        let service = ExecutionServiceImpl::new(5, 10);
        let request = create_test_request("AAPL", 100.0, OrderSide::SideSell);
        let start = Instant::now();

        let _ = service.execute_order(request).await;

        let elapsed = start.elapsed().as_millis() as u64;
        assert!(elapsed >= 5, "Elapsed {}ms should be >= 5ms", elapsed);
        assert!(elapsed <= 30, "Elapsed {}ms should be <= 30ms", elapsed);
    }

    #[tokio::test]
    async fn test_fill_price_slippage_calculation() {
        let base_price = 100.0;

        let fill_with_positive_slippage = calculate_fill_price(base_price, 0.001);
        assert!((fill_with_positive_slippage - 100.1).abs() < 0.0001);

        let fill_with_negative_slippage = calculate_fill_price(base_price, -0.001);
        assert!((fill_with_negative_slippage - 99.9).abs() < 0.0001);

        let fill_with_no_slippage = calculate_fill_price(base_price, 0.0);
        assert!((fill_with_no_slippage - 100.0).abs() < 0.0001);

        let fill_with_max_slippage = calculate_fill_price(base_price, 0.001);
        assert!(fill_with_max_slippage >= base_price * 0.999);
        assert!(fill_with_max_slippage <= base_price * 1.001);
    }

    #[tokio::test]
    async fn test_fill_price_within_slippage_bounds() {
        let service = ExecutionServiceImpl::new(1, 2);
        let base_price = get_mock_market_price("BTC");
        let max_slippage = 0.001;

        for _ in 0..10 {
            let request = create_test_request("BTC", 0.5, OrderSide::SideBuy);
            let response = service.execute_order(request).await.unwrap();
            let receipt = response.into_inner();

            let min_expected = base_price * (1.0 - max_slippage);
            let max_expected = base_price * (1.0 + max_slippage);

            assert!(
                receipt.fill_price >= min_expected && receipt.fill_price <= max_expected,
                "Fill price {} should be between {} and {}",
                receipt.fill_price,
                min_expected,
                max_expected
            );
        }
    }

    #[tokio::test]
    async fn test_order_id_uniqueness() {
        let service = ExecutionServiceImpl::new(1, 2);
        let mut order_ids = HashSet::new();

        for _ in 0..100 {
            let request = create_test_request("ETH", 1.0, OrderSide::SideBuy);
            let response = service.execute_order(request).await.unwrap();
            let receipt = response.into_inner();

            assert!(
                order_ids.insert(receipt.order_id.clone()),
                "Duplicate order ID found: {}",
                receipt.order_id
            );
        }

        assert_eq!(order_ids.len(), 100);
    }

    #[tokio::test]
    async fn test_order_id_is_valid_uuid() {
        let service = ExecutionServiceImpl::new(1, 2);
        let request = create_test_request("MSFT", 50.0, OrderSide::SideSell);

        let response = service.execute_order(request).await.unwrap();
        let receipt = response.into_inner();

        let parsed = Uuid::parse_str(&receipt.order_id);
        assert!(parsed.is_ok(), "Order ID should be a valid UUID");
    }

    #[test]
    fn test_get_mock_market_price_known_assets() {
        assert!((get_mock_market_price("BTC") - 45000.0).abs() < 0.01);
        assert!((get_mock_market_price("btc") - 45000.0).abs() < 0.01);
        assert!((get_mock_market_price("BTCUSD") - 45000.0).abs() < 0.01);
        assert!((get_mock_market_price("ETH") - 2500.0).abs() < 0.01);
        assert!((get_mock_market_price("ETHUSD") - 2500.0).abs() < 0.01);
        assert!((get_mock_market_price("AAPL") - 175.0).abs() < 0.01);
        assert!((get_mock_market_price("GOOGL") - 140.0).abs() < 0.01);
        assert!((get_mock_market_price("MSFT") - 380.0).abs() < 0.01);
        assert!((get_mock_market_price("SPY") - 450.0).abs() < 0.01);
    }

    #[test]
    fn test_get_mock_market_price_unknown_asset() {
        assert!((get_mock_market_price("UNKNOWN") - 100.0).abs() < 0.01);
        assert!((get_mock_market_price("XYZ") - 100.0).abs() < 0.01);
        assert!((get_mock_market_price("") - 100.0).abs() < 0.01);
    }

    #[test]
    fn test_calculate_fill_price() {
        assert!((calculate_fill_price(100.0, 0.0) - 100.0).abs() < 0.0001);
        assert!((calculate_fill_price(100.0, 0.01) - 101.0).abs() < 0.0001);
        assert!((calculate_fill_price(100.0, -0.01) - 99.0).abs() < 0.0001);
        assert!((calculate_fill_price(45000.0, 0.001) - 45045.0).abs() < 0.0001);
    }

    #[tokio::test]
    async fn test_execute_order_buy_side() {
        let service = ExecutionServiceImpl::new(1, 2);
        let request = create_test_request("AAPL", 10.0, OrderSide::SideBuy);

        let response = service.execute_order(request).await;

        assert!(response.is_ok());
        let receipt = response.unwrap().into_inner();
        assert_eq!(receipt.status, ExecutionStatus::Filled as i32);
    }

    #[tokio::test]
    async fn test_execute_order_sell_side() {
        let service = ExecutionServiceImpl::new(1, 2);
        let request = create_test_request("GOOGL", 25.0, OrderSide::SideSell);

        let response = service.execute_order(request).await;

        assert!(response.is_ok());
        let receipt = response.unwrap().into_inner();
        assert_eq!(receipt.status, ExecutionStatus::Filled as i32);
    }

    #[tokio::test]
    async fn test_timestamp_is_reasonable() {
        let service = ExecutionServiceImpl::new(1, 2);
        let before = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_millis() as i64;

        let request = create_test_request("SPY", 100.0, OrderSide::SideBuy);
        let response = service.execute_order(request).await.unwrap();
        let receipt = response.into_inner();

        let after = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_millis() as i64;

        assert!(
            receipt.timestamp >= before && receipt.timestamp <= after,
            "Timestamp {} should be between {} and {}",
            receipt.timestamp,
            before,
            after
        );
    }
}

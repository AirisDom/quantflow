pub mod quantflow {
    tonic::include_proto!("quantflow");
}

use quantflow::execution_service_server::{ExecutionService, ExecutionServiceServer};
use quantflow::{ExecutionReceipt, ExecutionStatus, OrderRequest, OrderSide};
use rand::Rng;
use std::env;
use std::time::{SystemTime, UNIX_EPOCH};
use tonic::{transport::Server, Request, Response, Status};
use tracing::{info, warn, error, Level, instrument};
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

        let mut rng = rand::thread_rng();

        let delay_ms = rng.gen_range(self.min_latency_ms..=self.max_latency_ms);
        tokio::time::sleep(tokio::time::Duration::from_millis(delay_ms)).await;

        let base_price = get_mock_market_price(&order.asset);
        let slippage = rng.gen_range(-0.001..=0.001);
        let fill_price = base_price * (1.0 + slippage);

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

fn get_mock_market_price(asset: &str) -> f64 {
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

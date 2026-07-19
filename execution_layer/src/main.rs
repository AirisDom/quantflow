pub mod quantflow {
    tonic::include_proto!("quantflow");
}

use quantflow::execution_service_server::{ExecutionService, ExecutionServiceServer};
use quantflow::{ExecutionReceipt, ExecutionStatus, OrderRequest, OrderSide};
use rand::Rng;
use std::env;
use std::time::{SystemTime, UNIX_EPOCH};
use tonic::{transport::Server, Request, Response, Status};
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
    async fn execute_order(
        &self,
        request: Request<OrderRequest>,
    ) -> Result<Response<ExecutionReceipt>, Status> {
        let order = request.into_inner();

        let side_str = match order.side() {
            OrderSide::SideBuy => "BUY",
            OrderSide::SideSell => "SELL",
        };
        println!(
            "[ExecutionService] Received order: asset={}, quantity={}, side={}",
            order.asset, order.quantity, side_str
        );

        if order.asset.is_empty() {
            println!("[ExecutionService] Error: asset cannot be empty");
            return Err(Status::invalid_argument("asset cannot be empty"));
        }

        if order.quantity <= 0.0 {
            println!("[ExecutionService] Error: quantity must be positive");
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

        println!(
            "[ExecutionService] Order executed: order_id={}, fill_price={:.4}, slippage={:+.4}%, latency={}ms",
            order_id, fill_price, slippage * 100.0, delay_ms
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

#[tokio::main]
async fn main() -> Result<(), Box<dyn std::error::Error>> {
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

    println!("[ExecutionService] Starting gRPC server on {}", addr);
    println!(
        "[ExecutionService] Latency simulation: {}ms - {}ms",
        min_latency_ms, max_latency_ms
    );

    Server::builder()
        .add_service(ExecutionServiceServer::new(service))
        .serve(addr)
        .await?;

    Ok(())
}

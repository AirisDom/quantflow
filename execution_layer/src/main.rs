pub mod quantflow {
    tonic::include_proto!("quantflow");
}

use quantflow::execution_service_server::{ExecutionService, ExecutionServiceServer};
use quantflow::{ExecutionReceipt, ExecutionStatus, OrderRequest};
use std::time::{SystemTime, UNIX_EPOCH};
use tonic::{transport::Server, Request, Response, Status};

#[derive(Debug, Default)]
pub struct ExecutionServiceImpl;

#[tonic::async_trait]
impl ExecutionService for ExecutionServiceImpl {
    async fn execute_order(
        &self,
        request: Request<OrderRequest>,
    ) -> Result<Response<ExecutionReceipt>, Status> {
        let order = request.into_inner();

        let delay_ms = 10 + (rand_u64() % 41);
        tokio::time::sleep(tokio::time::Duration::from_millis(delay_ms)).await;

        let slippage = (rand_u64() % 100) as f64 / 10000.0 - 0.005;
        let base_price = 100.0;
        let fill_price = base_price * (1.0 + slippage);

        let timestamp = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_millis() as i64;

        let order_id = format!("ORD-{}-{}", order.asset, timestamp);

        let receipt = ExecutionReceipt {
            order_id,
            fill_price,
            status: ExecutionStatus::Filled.into(),
            timestamp,
        };

        Ok(Response::new(receipt))
    }
}

fn rand_u64() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap()
        .subsec_nanos() as u64
}

#[tokio::main]
async fn main() -> Result<(), Box<dyn std::error::Error>> {
    let addr = "[::]:50052".parse()?;
    let service = ExecutionServiceImpl::default();

    println!("ExecutionService listening on {}", addr);

    Server::builder()
        .add_service(ExecutionServiceServer::new(service))
        .serve(addr)
        .await?;

    Ok(())
}

FROM rust:1.75 AS chef
RUN cargo install cargo-chef
WORKDIR /app

FROM chef AS planner
COPY execution_layer/Cargo.toml execution_layer/Cargo.lock ./
COPY execution_layer/src ./src
COPY execution_layer/build.rs ./
COPY shared/quantflow.proto /shared/
RUN cargo chef prepare --recipe-path recipe.json

FROM chef AS builder
RUN apt-get update && apt-get install -y --no-install-recommends protobuf-compiler && rm -rf /var/lib/apt/lists/*
COPY --from=planner /app/recipe.json recipe.json
COPY shared/quantflow.proto /shared/
COPY execution_layer/build.rs ./
RUN cargo chef cook --release --recipe-path recipe.json

COPY execution_layer/Cargo.toml execution_layer/Cargo.lock ./
COPY execution_layer/src ./src
RUN cargo build --release

FROM debian:bookworm-slim
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=builder /app/target/release/execution_layer .

ENV EXECUTION_SERVICE_PORT=50052
EXPOSE 50052

ENTRYPOINT ["./execution_layer"]

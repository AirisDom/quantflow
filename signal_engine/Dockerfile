FROM python:3.11-slim AS builder

WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends \
    build-essential \
    && rm -rf /var/lib/apt/lists/*

COPY signal_engine/requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt

COPY shared/quantflow.proto /shared/
RUN python -m grpc_tools.protoc \
    -I/shared \
    --python_out=. \
    --grpc_python_out=. \
    /shared/quantflow.proto

FROM python:3.11-slim

WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

COPY --from=builder /usr/local/lib/python3.11/site-packages /usr/local/lib/python3.11/site-packages
COPY --from=builder /app/quantflow_pb2.py /app/quantflow_pb2_grpc.py ./

COPY signal_engine/main.py signal_engine/__init__.py ./

ENV PYTHONUNBUFFERED=1
ENV SIGNAL_ENGINE_PORT=50051
ENV SIGNAL_ENGINE_HEALTH_PORT=8080

EXPOSE 50051 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["python", "main.py"]

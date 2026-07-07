.PHONY: generate clean install run

PROTO_SRC = ../shared/quantflow.proto
OUT_DIR = .

generate:
	python -m grpc_tools.protoc \
		-I../shared \
		--python_out=$(OUT_DIR) \
		--grpc_python_out=$(OUT_DIR) \
		$(PROTO_SRC)

clean:
	rm -f quantflow_pb2.py quantflow_pb2_grpc.py

install:
	pip install -r requirements.txt

run: generate
	python main.py

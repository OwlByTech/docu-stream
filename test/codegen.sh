#!/bin/bash

echo "Generating proto..."
PROTO_OUR_DIR="./proto"
PROTO_DIR="../proto"

rm -rf ${PROTO_OUR_DIR}
mkdir -p ${PROTO_OUR_DIR}
protoc --go_out=${PROTO_OUR_DIR} --go_opt=paths=source_relative \
  --go-grpc_out=${PROTO_OUR_DIR} --go-grpc_opt=paths=source_relative \
  -I${PROTO_DIR} \
  ${PROTO_DIR}/convert.proto \
  ${PROTO_DIR}/word.proto

#!/bin/bash

GRPC_SERVER="localhost:4014"

REQUEST_DATA=$(cat <<EOF
{
  "docu": {
    "chunks": ["ZG9jdS1zdHJlYW0="]
  }
}
EOF
)

grpcurl -plaintext\
  -proto ../proto/convert.proto \
  -import-path ../proto \
  -d "$REQUEST_DATA" \
  "$GRPC_SERVER" convert.Convert/WordToPdf

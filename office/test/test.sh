#!/bin/bash

grpcurl -plaintext -proto ../Protos/word.proto \
  -d '{
        "header": [{"key": "Company Name", "value": "OwlByTech"}],
        "body": [{"key": "Company Name", "value": "OwlByTech"}]
      }' \
  localhost:3000 word.Word.Apply

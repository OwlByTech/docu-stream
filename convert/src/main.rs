use std::pin::Pin;
use tokio_stream::{Stream, StreamExt};
use tonic::{transport::Server, Request, Response, Status, Streaming};

use convert::convert_server::{Convert, ConvertServer};
use convert::{WordToPdfReq, WordToPdfRes};

pub mod convert {
    tonic::include_proto!("convert");
}

pub mod word {
    tonic::include_proto!("word");
}

type ConvertResult<T> = Result<Response<T>, Status>;
type ResponseStream = Pin<Box<dyn Stream<Item = Result<WordToPdfRes, Status>> + Send>>;

#[derive(Default)]
pub struct ConvertService {}

#[tonic::async_trait]
impl Convert for ConvertService {
    type WordToPdfStream = ResponseStream;

    async fn word_to_pdf(
        &self,
        req: Request<Streaming<WordToPdfReq>>,
    ) -> ConvertResult<Self::WordToPdfStream> {
        let mut stream = req.into_inner();
        let mut chunks = Vec::new();

        while let Some(message) = stream.next().await {
            match message {
                Ok(req) => {
                    // Collect the chunks from each request
                    if let Some(docu_chunk) = req.docu {
                        chunks.extend(docu_chunk.chunks);
                    }
                }
                Err(e) => {
                    eprintln!("Error while receiving request: {:?}", e);
                    return Err(Status::internal("Error uploading stream"));
                }
            }
        }

        // TODO: save the file and the process with libreoffice command

        let response = WordToPdfRes {
            docu: Some(word::DocuChunk { chunks }),
        };

        let response_stream = tokio_stream::iter(vec![Ok(response)]);

        Ok(Response::new(
            Box::pin(response_stream) as Self::WordToPdfStream
        ))
    }
}

#[tokio::main]
async fn main() -> Result<(), Box<dyn std::error::Error>> {
    let port = 4014;
    let addr = format!("0.0.0.0:{}", port).parse().unwrap();

    let convert_service = ConvertService::default();

    println!("Server running on {}", addr);

    Server::builder()
        .add_service(ConvertServer::new(convert_service))
        .serve(addr)
        .await?;

    Ok(())
}

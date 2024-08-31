use std::fs::{remove_file, File};
use std::io::{self, Read, Write};
use std::path::Path;
use std::pin::Pin;
use std::process::Command;
use std::time::{SystemTime, UNIX_EPOCH};
use tokio::sync::mpsc;
use tokio_stream::wrappers::ReceiverStream;
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

        let timestamp = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .expect("Time went backwards")
            .as_secs();
        let input_file_path = format!("input_{}.docx", timestamp);

        let mut file = match File::create(&input_file_path) {
            Ok(f) => f,
            Err(e) => {
                eprintln!("Error creating file: {:?}", e);
                return Err(Status::internal("Error creating file"));
            }
        };

        while let Some(message) = stream.next().await {
            match message {
                Ok(req) => {
                    if let Some(docu_chunk) = req.docu {
                        if let Err(e) = save_chunk_to_file(&mut file, &docu_chunk.chunks).await {
                            eprintln!("Error saving chunk to file: {:?}", e);
                            let _ = remove_file(&input_file_path);
                            return Err(Status::internal("Error saving file"));
                        }
                    }
                }
                Err(e) => {
                    eprintln!("Error while receiving request: {:?}", e);
                    let _ = remove_file(&input_file_path);
                    return Err(Status::internal("Error uploading stream"));
                }
            }
        }

        let pdf_path = format!("output_{}.pdf", timestamp);
        if let Err(e) = convert_to_pdf(&input_file_path, &pdf_path).await {
            eprintln!("Error converting file: {:?}", e);
            let _ = remove_file(&input_file_path);
            let _ = remove_file(&pdf_path);
            return Err(Status::internal("Error converting file to PDF"));
        }

        let _ = remove_file(&input_file_path);

        let mut file = match File::open(&pdf_path) {
            Ok(f) => f,
            Err(e) => {
                eprintln!("Error opening file for reading: {:?}", e);
                let _ = remove_file(&pdf_path);
                return Err(Status::internal("Error opening file for reading"));
            }
        };

        let (tx, rx) = mpsc::channel(1);

        tokio::spawn(async move {
            let mut buffer = [0; 1024];
            loop {
                match file.read(&mut buffer) {
                    Ok(bytes_read) if bytes_read > 0 => {
                        let chunk = buffer[..bytes_read].to_vec();
                        let response = WordToPdfRes {
                            docu: Some(word::DocuChunk {
                                chunks: vec![chunk],
                            }),
                        };

                        if tx.send(Ok(response)).await.is_err() {
                            eprintln!("Failed to send response chunk");
                            break;
                        }
                    }
                    Ok(_) => {
                        break;
                    }
                    Err(e) => {
                        eprintln!("Error reading file: {:?}", e);
                        break;
                    }
                }
            }

            let _ = remove_file(&pdf_path);
        });

        Ok(Response::new(Box::pin(ReceiverStream::new(rx))))
    }
}

async fn save_chunk_to_file(file: &mut File, chunks: &[Vec<u8>]) -> io::Result<()> {
    for chunk in chunks {
        file.write_all(&chunk)?;
    }
    Ok(())
}

async fn convert_to_pdf(input_path: &str, output_path: &str) -> io::Result<()> {
    let mut child = Command::new("libreoffice")
        .arg("--headless")
        .arg("--convert-to")
        .arg("pdf")
        .arg(input_path)
        .arg("--outdir")
        .arg(".")
        .spawn()?;

    let status = child.wait()?;

    if !status.success() {
        return Err(io::Error::new(
            io::ErrorKind::Other,
            "LibreOffice conversion failed",
        ));
    }

    let pdf_output = input_path.replace(".docx", ".pdf");
    if Path::new(&pdf_output).exists() {
        std::fs::rename(&pdf_output, &output_path)?;
    }

    Ok(())
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

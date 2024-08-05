# Docu Stream

A template engine for multiple documents. 

## Getting started
To get started, you must build the Docker image:

```bash
docker build -t docu-stream:latest -f Dockerfile .
```
Then, run the image on a custom port:

```bash
docker run -it --rm -p 5000:3000 docu-stream:latest
```

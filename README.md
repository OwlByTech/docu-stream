# Docu Stream

A template engine for multiple documents. 

## Getting started

### Docustream
To get started, you must build the Docker image:
```bash
docker build -t docu-stream:latest -f Dockerfile-docustream .
```
Then, run the image on a custom port:

```bash
docker run -it --rm -p 5000:3000 docu-stream:latest
```

### Convert
To get started, you must build the Docker image:
```bash
docker build -t docu-convert:latest -f Dockerfile-convert .
```
Then, run the image on a custom port:

```bash
docker run -it --rm -p 5001:4014 docu-convert:latest
```

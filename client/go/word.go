package docustream

import (
	"context"

	"github.com/owlbytech/docu-stream/internal/proto"
	"google.golang.org/grpc"
	"google.golang.org/grpc/credentials/insecure"
)

type ConnectOptions struct {
	Url string
}


type WordClient = proto.WordClient
type WordApplyReq = proto.WordApplyReq
type WordApplyRes = proto.WordApplyRes
type DocuStringValues = proto.DocuStringValues

type Word struct {
	client WordClient
}


func NewWordClient(c *ConnectOptions) (*Word, error) {
	conn, err := grpc.NewClient(c.Url, grpc.WithTransportCredentials(insecure.NewCredentials()))
	if err != nil {
		return nil, err
	}
	client := proto.NewWordClient(conn)

	return &Word{
		client: client,
	}, nil
}

func (w *Word) Apply(req *WordApplyReq) (*WordApplyRes, error) {
	ctx := context.Background()
	res, err := w.client.Apply(ctx, req)
	if err != nil {
		return nil, err
	}
	return res, nil
}

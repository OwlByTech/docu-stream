package docustream

import (
	"testing"

	"github.com/stretchr/testify/assert"
)

func TestWordApplyService(t *testing.T) {
	c, err := NewWordClient(&ConnectOptions{Url: "localhost:3000"})
	assert.Nil(t, err)
	assert.NotNil(t, c)

	res, err := c.Apply(&WordApplyReq{
		Header: []*DocuStringValues{
			{Key: "Company Name", Value: "OwlByTech"},
		},
		Body: []*DocuStringValues{
			{Key: "Company Name", Value: "OwlByTech"},
		},
	})

	assert.Nil(t, err)
	assert.NotNil(t, res)

	t.Log(res)
}

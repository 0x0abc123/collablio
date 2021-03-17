#!/bin/bash


if [[ -z $(docker image ls | grep 'dgraph/standalone') ]]
then 
	echo "running: docker pull dgraph/standalone"
	docker pull dgraph/standalone
fi

echo "starting up dgraph docker standalone"
bash ./run.sh

sleep 30

echo "setting up database schema..."

curl "http://127.0.0.1:8080/alter" -XPOST -d $'
  ty: string @index(hash) .
  l: string @index(term, trigram) .
  d: string @index(term, trigram) .
  c: string @index(trigram) .
  x: string @index(trigram) .
  b: string .
  e: string @index(hash) .
  m: datetime @index(hour) .
  t: datetime @index(hour) .
  acl: [string] @index(hash) .
  out: [uid] @reverse .
  lnk: [uid] @reverse .
  ro: string @index(hash) .

  type N {
	ty
	l 
	d
	c	
	x 
	b 
	e 
	m 
	t 
	acl 
	out 
	lnk
	ro
  }
'

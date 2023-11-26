#!/bin/bash
# assume dgraph standalone has been started
echo "setting up database schema..."

curl "http://127.0.0.1:8080/alter" -XPOST -d $'
  ty: string @index(hash) .
  l: string @index(trigram, term) .
  d: string @index(trigram, term) .
  c: string @index(trigram) .
  x: string @index(trigram, term) .
  b: string .
  e: string @index(hash) .
  m: datetime @index(hour) .
  t: datetime @index(hour) .
  a: string @index(term) .
  s: string @index(term) .
  n: float .
  out: [uid] @reverse .
  lnk: [uid] @reverse .
  ro: string @index(hash) .
  username: string @index(hash) .
  password: string @index(hash) .
  
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
	a
    s
    n
	out 
	lnk
	ro
  }
  
  type U {
	username
	password
  }
'

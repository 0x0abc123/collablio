curl "http://10.3.3.60:8080/alter" -XPOST -d $'
  ty: string @index(hash) .
  l: string @index(term) .
  d: string @index(term) .
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
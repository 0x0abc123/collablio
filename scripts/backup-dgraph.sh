#!/bin/bash
TSTMP=$(date +%s)
curl -H 'Content-type: application/graphql' 127.0.0.1:8080/admin -d $'mutation { export(input: { format: "json" }) { response { message code }}}'
tar zcf /tmp/secquiry-dgraph-backup-$TSTMP.tar.gz /dgraph/export
rm -rf /dgraph/export
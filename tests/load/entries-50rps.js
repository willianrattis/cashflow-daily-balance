// k6 load test — CashFlowEntryService write path at the assumed peak (A-013/NFR-011/NFR-013, docs/01).
// Run via the official k6 Docker image, attached to the compose network — see scripts/load/run.sh,
// which also generates the token.
//
// A-013 calls for 50 lançamentos/s for 15 minutes; this script defaults to a shorter 2m run so a
// routine evidence refresh stays fast. To reproduce the full 15-minute assumption:
//   scripts/load/run.sh entries 15m
//
// Required env vars: BASE_URL, TOKEN.
// Optional: DURATION (default 2m).
import http from 'k6/http';
import { check } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5101';
const TOKEN = __ENV.TOKEN;
const DURATION = __ENV.DURATION || '2m';

if (!TOKEN) {
  throw new Error('TOKEN is a required env var.');
}

export const options = {
  // p(99) is not in k6's default summary export (only p(90)/p(95)) even though the p(99)
  // threshold below is still evaluated either way — added explicitly so the report script can
  // read the actual p(99) value, not just its threshold pass/fail.
  summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(90)', 'p(95)', 'p(99)'],
  scenarios: {
    entries_peak: {
      executor: 'constant-arrival-rate',
      // A-013: 50 lançamentos/s is the assumed write peak.
      rate: 50,
      timeUnit: '1s',
      duration: DURATION,
      // See consolidation-50rps.js for why this is well above the target rate.
      preAllocatedVUs: 100,
      maxVUs: 300,
    },
  },
  thresholds: {
    // NFR-013 (Lançamentos, condições normais). No http_req_failed gate here on purpose —
    // unlike the Consolidado (NFR-002/003), the challenge sets no documented loss budget for
    // writes; `checks` below still reports the 201 rate for visibility in the summary.
    http_req_duration: ['p(95)<300', 'p(99)<1000'],
    // See consolidation-50rps.js: forces k6 to actually keep up with the 50/s arrival rate.
    dropped_iterations: ['count==0'],
  },
};

export default function () {
  // __VU/__ITER/Date.now() combined are unique enough across a 2-15 minute run at 50/s without
  // pulling in a jslib UUID dependency (k6's official image has no outbound network dependency
  // beyond the server under test).
  const uniqueId = `${__VU}-${__ITER}-${Date.now()}`;
  const payload = JSON.stringify({
    amountInCents: 1000 + (__ITER % 500),
    nature: __ITER % 2 === 0 ? 'Credit' : 'Debit',
  });

  const res = http.post(`${BASE_URL}/api/v1/entries`, payload, {
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${TOKEN}`,
      'Idempotency-Key': `load-key-${uniqueId}`,
    },
    tags: { name: 'RecordEntry' },
  });

  check(res, { 'status is 201': (r) => r.status === 201 });
}

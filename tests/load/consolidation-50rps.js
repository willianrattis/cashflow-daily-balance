// k6 load test — ConsolidationService at the documented peak (NFR-002/NFR-003, docs/01).
// Run via the official k6 Docker image, attached to the compose network — see
// scripts/load/run.sh, which also seeds the history before this runs
// (scripts/load/seed-consolidation-history.sh) and generates the token.
//
// Required env vars: BASE_URL, TOKEN, FROM_DATE, TO_DATE.
// Optional: DURATION (default 2m).
import http from 'k6/http';
import { check } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5102';
const TOKEN = __ENV.TOKEN;
const FROM_DATE = __ENV.FROM_DATE;
const TO_DATE = __ENV.TO_DATE;
const DURATION = __ENV.DURATION || '2m';

if (!TOKEN || !FROM_DATE || !TO_DATE) {
  throw new Error('TOKEN, FROM_DATE and TO_DATE are required env vars.');
}

export const options = {
  // p(99) is not in k6's default summary export (only p(90)/p(95)) even though the p(99)
  // threshold below is still evaluated either way — added explicitly so the report script can
  // read the actual p(99) value, not just its threshold pass/fail.
  summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(90)', 'p(95)', 'p(99)'],
  scenarios: {
    consolidation_peak: {
      executor: 'constant-arrival-rate',
      // NFR-002: 50 req/s is the documented peak for the Consolidado.
      rate: 50,
      timeUnit: '1s',
      duration: DURATION,
      // Generous headroom over the 50/s target so a slow response never forces k6 itself
      // to drop an iteration for lack of a free VU — a VU shortage would corrupt the
      // dropped_iterations==0 check below with a false positive unrelated to the server.
      preAllocatedVUs: 100,
      maxVUs: 300,
    },
  },
  thresholds: {
    // NFR-003/A-004: "perda" means a failed request (5xx, timeout, connection error, or a
    // non-2xx status), never a lost financial movement — the Consolidado is a read-only
    // projection, so there is nothing to lose here besides the response itself.
    http_req_failed: ['rate<0.05'],
    // NFR-013 (Consolidado, condições normais).
    http_req_duration: ['p(95)<500', 'p(99)<2000'],
    // k6 drops a scheduled iteration when it cannot start it in time (VU/arrival-rate
    // starvation) — any drop means the generator itself fell behind, which would silently
    // understate the real request rate reaching the server.
    dropped_iterations: ['count==0'],
  },
};

export default function () {
  const url = `${BASE_URL}/api/v1/consolidation?fromDate=${FROM_DATE}&toDate=${TO_DATE}`;
  const res = http.get(url, {
    headers: { Authorization: `Bearer ${TOKEN}` },
    tags: { name: 'GetConsolidation' },
  });

  check(res, { 'status is 200': (r) => r.status === 200 });
}

"""Pure-stdlib tests for the opt-in HTTP smoke harness; no Aspire claims."""
import io
import json
import unittest
import urllib.request
from decimal import Decimal
from unittest.mock import patch

import runtime_http_smoke as smoke


class FakeRuntime:
    def __init__(self):
        self.count = 2
        self.total = Decimal("17")
        self.receipts = {}
        self.sent = []

    def exchange(self, base, method, path, value=None, key=None):
        self.sent.append((method, path, value, key))
        if method == "GET":
            return 200, {"count": self.count, "sum": str(self.total)}
        amount = Decimal(value)
        if key in self.receipts:
            original, receipt = self.receipts[key]
            return ((200, {"id": receipt}) if original == amount else
                    (409, {"title": "Idempotency key conflict"}))
        receipt = "receipt-" + str(len(self.receipts) + 1)
        self.receipts[key] = (amount, receipt)
        self.count += 1
        self.total += amount
        return 201, {"id": receipt, "value": str(amount)}


class HttpSmokeTests(unittest.TestCase):
    def test_smoke_checks_first_write_replay_conflict_and_count_sum_deltas(self):
        fake = FakeRuntime()
        with patch.object(smoke, "exchange", side_effect=fake.exchange):
            observed = smoke.run("http://ingestion", "http://consolidation", 1, 0.01)
        self.assertEqual(observed["before"], {"count": 2, "sum": "17"})
        self.assertEqual(observed["happy"]["post_status"], 201)
        self.assertEqual(observed["happy"]["after_http"]["count"], 3)
        self.assertEqual(observed["duplicate_http"]["statuses"], [201, 200, 409])
        self.assertEqual(observed["duplicate_http"]["after_http"]["count"], 4)
        self.assertEqual(Decimal(observed["duplicate_http"]["after_http"]["sum"]),
                         Decimal("32.5"))
        self.assertEqual(len(fake.receipts), 2)

    def test_http_post_serializes_decimal_as_json_number_not_string(self):
        class Response:
            status = 201
            def __enter__(self):
                return self
            def __exit__(self, *args):
                return False
            def read(self):
                return b'{"id":"receipt-1"}'
        with patch.object(urllib.request, "urlopen", return_value=Response()) as send:
            status, _ = smoke.exchange("http://localhost", "POST", "/values", "5.00", "key")
        self.assertEqual(status, 201)
        sent = send.call_args.args[0]
        self.assertEqual(sent.data, b'{"value":5.00}')
        self.assertEqual(json.loads(sent.data)["value"], 5.0)
        self.assertEqual(sent.headers["Idempotency-key"], "key")

    def test_poll_timeout_is_not_misreported_as_success(self):
        with patch.object(smoke, "snapshot", return_value={"count": 0, "sum": "0"}):
            with patch.object(smoke.time, "monotonic", side_effect=[0, 1]):
                with self.assertRaisesRegex(AssertionError, "Timed out"):
                    smoke.expect_total("http://consolidation", 1, Decimal("10.5"), 0.5, 0.01)


if __name__ == "__main__":
    unittest.main()

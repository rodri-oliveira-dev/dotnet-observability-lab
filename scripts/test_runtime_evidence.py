"""Offline safety/regression checks for the opt-in live evidence runner.

These tests use fake observations and NEVER count as evidence of Aspire runtime.
"""
import importlib.util
import pathlib
import unittest
from unittest.mock import patch
from decimal import Decimal

SCRIPT = pathlib.Path(__file__).with_name("runtime_evidence.py")
spec = importlib.util.spec_from_file_location("runtime_evidence", SCRIPT)
import sys
sys.path.insert(0, str(SCRIPT.parent))
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class RuntimeEvidenceContractTests(unittest.TestCase):
    def test_http_and_broker_endpoints_must_be_local(self):
        for url in ("http://localhost:8080", "http://127.0.0.1:15672", "http://[::1]:9999"):
            runner.require_local(url)
        for url in ("http://example.com", "http://192.168.0.2", "file:///tmp/x",
                    "https://evil.example.org/localhost"):
            with self.assertRaises(ValueError):
                runner.require_local(url)

    def test_disposable_environment_requires_explicit_opt_in(self):
        with patch.dict("os.environ", {}, clear=True):
            with self.assertRaises(ValueError):
                runner.require_disposable()
        with patch.dict("os.environ", {"ASPIRE_DISPOSABLE_LAB": "I_UNDERSTAND"}, clear=True):
            runner.require_disposable()

    def test_snapshots_drop_payload_trace_and_idempotency_key(self):
        observed = {"value_count": 1, "pending": 0, "rows": [
            {"event_id": "e", "payload": "secret payload", "traceparent": "00-secret",
             "idempotency_key": "personal data", "published": True}]}
        result = runner.safe_db(observed)
        self.assertEqual(result["rows"], [{"event_id": "e", "published": True}])
        self.assertNotIn("secret payload", str(result))
        self.assertNotIn("00-secret", str(result))

    def test_scenario_selectors_are_all_six_and_never_fake_telemetry(self):
        self.assertEqual(set(runner.SCENARIOS), set(range(1, 7)))
        self.assertIn("NOT VERIFIED", SCRIPT.read_text(encoding="utf-8"))
        self.assertIn("NOT INSPECTED", SCRIPT.read_text(encoding="utf-8"))

    def test_main_wires_env_endpoints_and_writes_partial_evidence(self):
        import json
        import tempfile

        with tempfile.TemporaryDirectory() as temp:
            output = pathlib.Path(temp) / "evidence.json"
            env = {
                "ASPIRE_DISPOSABLE_LAB": "I_UNDERSTAND",
                "INGESTION_API_URL": "http://localhost:5010",
                "CONSOLIDATION_API_URL": "http://localhost:5020",
            }
            with patch.dict("os.environ", env, clear=True), \
                    patch.object(runner, "scenario1") as scenario:
                exit_code = runner.main(["--scenario", "1", "--output", str(output)])
            scenario.assert_called_once()
            args, record = scenario.call_args.args
            self.assertEqual((args.ingestion, args.consolidation),
                             ("http://localhost:5010", "http://localhost:5020"))
            self.assertEqual(record["scenario"], 1)
            self.assertEqual(exit_code, 0)
            evidence = json.loads(output.read_text(encoding="utf-8"))
            self.assertEqual(evidence["result"], "PARTIAL_EVIDENCE")
            self.assertEqual(evidence["runtime_acceptance"], "NOT VERIFIED")
            self.assertEqual(evidence["dashboard"]["traces"], "NOT INSPECTED")

    def test_main_cli_endpoints_override_environment(self):
        import tempfile

        with tempfile.TemporaryDirectory() as temp:
            output = pathlib.Path(temp) / "evidence.json"
            with patch.dict("os.environ", {"ASPIRE_DISPOSABLE_LAB": "I_UNDERSTAND"}, clear=True), \
                    patch.object(runner, "scenario2") as scenario:
                exit_code = runner.main([
                    "--scenario", "2", "--ingestion-url", "http://127.0.0.1:6010",
                    "--consolidation-url", "http://127.0.0.1:6020",
                    "--output", str(output),
                ])
            args = scenario.call_args.args[0]
            self.assertEqual((args.ingestion, args.consolidation),
                             ("http://127.0.0.1:6010", "http://127.0.0.1:6020"))
            self.assertEqual(exit_code, 0)

    def test_main_redacts_internal_attribute_error_and_preserves_evidence(self):
        import json
        import tempfile

        with tempfile.TemporaryDirectory() as temp:
            output = pathlib.Path(temp) / "evidence.json"
            with patch.dict("os.environ", {"ASPIRE_DISPOSABLE_LAB": "I_UNDERSTAND"}, clear=True), \
                    patch.object(runner, "scenario1", side_effect=AttributeError("sensitive detail")):
                exit_code = runner.main([
                    "--scenario", "1", "--ingestion-url", "http://localhost:5010",
                    "--consolidation-url", "http://localhost:5020",
                    "--output", str(output),
                ])
            evidence = json.loads(output.read_text(encoding="utf-8"))
            self.assertEqual(exit_code, 1)
            self.assertEqual(evidence["result"], "FAIL_OR_BLOCKED")
            self.assertEqual(evidence["error_type"], "AttributeError")
            self.assertNotIn("sensitive detail", output.read_text(encoding="utf-8"))

    def test_operator_checkpoint_prints_newline_not_literal_backslash_n(self):
        from io import StringIO

        output = StringIO()
        with patch.object(runner.sys, "stderr", output), patch("builtins.input", return_value=""):
            runner.operator_checkpoint("Observe the local resources.")
        self.assertTrue(output.getvalue().startswith("\nOPERATOR ACTION"))
        self.assertNotIn("\\nOPERATOR ACTION", output.getvalue())

    def test_replay_must_keep_original_message_id(self):
        import json
        from io import BytesIO
        from unittest.mock import MagicMock
        fake = MagicMock()
        fake.status = 200
        fake.__enter__.return_value = fake
        fake.__exit__.return_value = False
        fake.read.return_value = b'{"routed":true}'
        with patch.dict("os.environ", {
            "RABBITMQ_MANAGEMENT_URL": "http://localhost:15672",
            "RABBITMQ_USERNAME": "guest", "RABBITMQ_PASSWORD": "hidden",
        }):
            with patch.object(runner.urllib.request, "urlopen", return_value=fake) as urlopen:
                runner.replay(None, {"event_id": "original-event", "payload": '{"Value":10}'})
                self.assertEqual(urlopen.call_count, 2)
                for call in urlopen.call_args_list:
                    request = call.args[0]
                    payload = json.loads(request.data)
                    self.assertEqual(payload["properties"]["message_id"], "original-event")
                    self.assertEqual(payload["properties"]["type"], "ValueReceived.v1")
                    self.assertEqual(payload["payload_encoding"], "string")


if __name__ == "__main__":
    unittest.main()

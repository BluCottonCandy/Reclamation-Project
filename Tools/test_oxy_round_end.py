"""Offline safety checks for the deployment handshake embedded in the workflow."""
import ast
from pathlib import Path
import textwrap
import unittest
from unittest.mock import Mock


class Clock:
    now = 0

    def monotonic(self):
        return self.now

    def sleep(self, seconds):
        self.now += seconds


def workflow_functions():
    text = (Path(__file__).parents[1] / '.github/workflows/oxy-manual.yml').read_text(encoding='utf-8-sig')
    script = text.rsplit("python3 - <<'PY'", 1)[1].split('\n          PY', 1)[0]
    tree = ast.parse(textwrap.dedent(script))
    tree.body = [node for node in tree.body if not isinstance(node, ast.If)]
    namespace = {'__name__': 'offline_test'}
    exec(compile(tree, '<workflow>', 'exec'), namespace)
    namespace['secrets'] = Mock(token_hex=Mock(return_value='a' * 32))
    namespace['time'] = Clock()
    return namespace


class RoundEndHandshakeTest(unittest.TestCase):
    def run_case(self, receipt_state=None, power_state='running', transition=None, limit=120):
        ns = workflow_functions()
        receipt = {'ticket': 'old', 'state': receipt_state} if receipt_state else None
        calls = []
        def api(path, method='GET', payload=None):
            nonlocal receipt
            calls.append((path, method, payload))
            if path == '/command' and 'queue' in payload['command']:
                if receipt_state != 'unsupported':
                    receipt = {'ticket': 'a' * 32, 'state': receipt_state or 'pending'}
                return None
            if path.startswith('/files/contents'):
                return receipt.copy() if receipt else None
            raise AssertionError('Unexpected API operation: ' + path)
        def listing(path):
            return {'reclamation-update.json': {}} if receipt else {}
        def state():
            nonlocal receipt
            if transition and ns['time'].now >= 10:
                receipt = {'ticket': 'a' * 32, 'state': transition}
                return power_state
            return 'running'
        return ns, calls, lambda: ns['wait_for_round_end'](api, listing, state, limit)

    def test_matching_round_boundary_and_offline_state_allow_deployment(self):
        ns, calls, run = self.run_case(power_state='offline', transition='ready')
        run()
        self.assertTrue(any('queue' in (payload or {}).get('command', '') for _, _, payload in calls))
        self.assertFalse(any(path == '/power' for path, _, _ in calls))

    def test_unexpected_shutdown_never_counts_as_a_round_boundary(self):
        ns, calls, run = self.run_case(power_state='offline', transition='pending')
        with self.assertRaises(ns['StageError']):
            run()
        self.assertFalse(any(path == '/power' for path, _, _ in calls))

    def test_missing_support_does_not_stop_the_server(self):
        ns, calls, run = self.run_case(receipt_state='unsupported')
        with self.assertRaisesRegex(ns['StageError'], 'did not acknowledge'):
            run()
        self.assertTrue(any('cancel' in (payload or {}).get('command', '') for _, _, payload in calls))

    def test_cancelled_request_aborts_without_power_action(self):
        ns, calls, run = self.run_case(transition='cancelled')
        with self.assertRaises(ns['StageError']):
            run()

    def test_long_round_times_out_without_force_stop(self):
        ns, calls, run = self.run_case(limit=90)
        with self.assertRaisesRegex(ns['StageError'], 'five hours'):
            run()
        self.assertFalse(any(path == '/power' for path, _, _ in calls))


if __name__ == '__main__':
    unittest.main()

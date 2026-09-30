"""Gate tests are synthetic and require no SDK/database. Run by the independent verifier."""
import pathlib
import tempfile
import unittest
import xml.etree.ElementTree as ET

import audit


class TrxGateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.path = pathlib.Path(self.temp.name) / 'contracts.trx'
        self.host = {'host_exit_code': 0, 'timed_out': False}
        namespace = '{' + audit.NS['t'] + '}'
        self.root = ET.Element(namespace + 'TestRun')
        results = ET.SubElement(self.root, namespace + 'Results')
        ET.SubElement(results, namespace + 'UnitTestResult', outcome='Passed', testName='Contract(value: synthetic-private-marker)')
        self.summary = ET.SubElement(self.root, namespace + 'ResultSummary', outcome='Completed')
        self.counts = ET.SubElement(self.summary, namespace + 'Counters',
                                   **{key: '1' if key in ('total', 'executed', 'passed') else '0' for key in audit.COUNTERS})
        self.namespace = namespace

    def tearDown(self):
        self.temp.cleanup()

    def save(self):
        ET.ElementTree(self.root).write(self.path)

    def test_complete_trx_and_zero_host_exit_pass(self):
        self.save()
        _, result = audit.strict_trx(self.path, self.host)
        self.assertEqual(1, result['passed'])

    def test_green_partial_trx_cannot_override_host_crash(self):
        self.save()
        self.host['host_exit_code'] = 139
        with self.assertRaises(audit.Rejected): audit.strict_trx(self.path, self.host)

    def test_missing_counter_rejects(self):
        self.counts.attrib.pop('aborted')
        self.save()
        with self.assertRaises(audit.Rejected): audit.strict_trx(self.path, self.host)

    def test_skip_aborted_and_warning_each_reject(self):
        for key in ('notExecuted', 'aborted', 'warning'):
            self.counts.set(key, '1')
            self.save()
            with self.assertRaises(audit.Rejected): audit.strict_trx(self.path, self.host)
            self.counts.set(key, '0')

    def test_result_count_mismatch_rejects(self):
        self.counts.set('total', '2'); self.counts.set('executed', '2'); self.counts.set('passed', '2')
        self.save()
        with self.assertRaises(audit.Rejected): audit.strict_trx(self.path, self.host)

    def test_runinfo_error_rejects_even_with_green_counters(self):
        infos = ET.SubElement(self.summary, self.namespace + 'RunInfos')
        ET.SubElement(infos, self.namespace + 'RunInfo', severity='Error')
        self.save()
        with self.assertRaises(audit.Rejected): audit.strict_trx(self.path, self.host)

    def test_published_trx_strips_output_and_argument_values(self):
        output = ET.SubElement(self.summary, self.namespace + 'Output')
        ET.SubElement(output, self.namespace + 'StdOut').text = 'synthetic-private-marker'
        target = pathlib.Path(self.temp.name) / 'safe.trx'
        audit.safe_trx(self.root, target)
        text = target.read_text()
        self.assertNotIn('synthetic-private-marker', text)
        self.assertIn('outcome="Passed"', text)
        self.assertIn('testName="Contract"', text)


if __name__ == '__main__': unittest.main()

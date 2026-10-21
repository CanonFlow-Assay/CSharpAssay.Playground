#!/usr/bin/env python3
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('csv_review', Path(__file__).with_name('prepare-csvhelper-review.py'))
review = importlib.util.module_from_spec(spec)
spec.loader.exec_module(review)


class ReviewBoundaryTests(unittest.TestCase):
    def response(self):
        return {'packetSha256': 'bound', 'reviewer': 'fixture reviewer', 'reviews': [{'id': 'one', 'decision': 'accept', 'rationale': 'fixture rationale'}]}

    def check(self, response):
        return review.validate_responses(response, {'items': [{'id': 'one'}]}, 'bound')

    def test_missing_identity(self):
        value = self.response(); value['reviewer'] = ''
        with self.assertRaises(ValueError): self.check(value)

    def test_stale_packet(self):
        value = self.response(); value['packetSha256'] = 'stale'
        with self.assertRaises(ValueError): self.check(value)

    def test_incomplete_items(self):
        value = self.response(); value['reviews'] = []
        with self.assertRaises(ValueError): self.check(value)

    def test_duplicate_items(self):
        value = self.response(); value['reviews'] *= 2
        with self.assertRaises(ValueError): self.check(value)

    def test_cannot_grant_pass(self):
        value = self.response(); value['reviews'][0]['decision'] = 'Pass'
        with self.assertRaises(ValueError): self.check(value)

    def test_responses_do_not_change_verdict_or_denominator(self):
        result = self.check(self.response())
        self.assertFalse(result['verificationVerdictChanged'])
        self.assertFalse(result['mutationDenominatorChanged'])
        self.assertIn('Self-reported', result['identityVerification'])


if __name__ == '__main__':
    unittest.main()

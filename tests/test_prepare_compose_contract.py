"""Regression guards for the fixed Web-only production candidate builder."""
import copy
import importlib.util
from pathlib import Path
import unittest

source = Path(__file__).resolve().parents[1] / 'tools/remote-deploy/prepare-web-only-20261016.py'
spec = importlib.util.spec_from_file_location('web_only_builder', source)
builder = importlib.util.module_from_spec(spec)
spec.loader.exec_module(builder)


class CandidateComposeTests(unittest.TestCase):
    def setUp(self):
        self.web = 'sha256:' + 'a' * 64
        self.db = 'sha256:' + 'b' * 64
        self.target = 'sha256:' + 'c' * 64
        self.base = {'name': 'catalog', 'services': {
            'web': {'image': self.web, 'read_only': True, 'cap_drop': ['ALL'],
                    'environment': {'IntakeReview__Enabled': 'true'},
                    'volumes': [{'type': 'volume', 'source': 'render-cache', 'target': '/var/cache/lips-render'}]},
            'postgres': {'image': self.db, 'volumes': [{'type': 'volume', 'source': 'pg-data', 'target': '/var/lib/postgresql/data'}]}},
            'volumes': {'pg-data': {'external': True}, 'render-cache': {'external': True}},
            'secrets': {'db_password': {'file': '/etc/catalog/secrets/db_password'}}}

    def build(self, base=None, image=None):
        return builder.candidate_compose(base or self.base, image or self.target, self.web, self.db)

    def test_only_web_image_changes(self):
        result = self.build()
        self.assertEqual(result['services']['web']['image'], self.target)
        result['services']['web']['image'] = self.web
        self.assertEqual(result, self.base)

    def test_input_and_nested_data_are_not_mutated(self):
        original = copy.deepcopy(self.base)
        result = self.build()
        result['services']['web']['environment']['IntakeReview__Enabled'] = 'false'
        self.assertEqual(self.base, original)

    def test_wrong_current_web_rejected(self):
        self.base['services']['web']['image'] = self.db
        with self.assertRaises(ValueError): self.build()

    def test_wrong_current_database_rejected(self):
        self.base['services']['postgres']['image'] = self.web
        with self.assertRaises(ValueError): self.build()

    def test_added_service_rejected(self):
        self.base['services']['extra'] = {'image': self.web}
        with self.assertRaises(ValueError): self.build()

    def test_missing_service_rejected(self):
        del self.base['services']['postgres']
        with self.assertRaises(ValueError): self.build()

    def test_web_build_rejected(self):
        self.base['services']['web']['build'] = '.'
        with self.assertRaises(ValueError): self.build()

    def test_database_build_rejected(self):
        self.base['services']['postgres']['build'] = '.'
        with self.assertRaises(ValueError): self.build()

    def test_mutable_image_rejected(self):
        with self.assertRaises(ValueError): self.build(image='candidate:latest')


if __name__ == '__main__':
    unittest.main()

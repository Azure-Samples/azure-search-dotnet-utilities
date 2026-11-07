import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time
import unittest
import urllib.error
import urllib.parse
import urllib.request
import uuid


API_VERSION = "2024-03-01-Preview"


class DocumentRoundtripTests(unittest.TestCase):
    def setUp(self):
        self.services = {}
        for role in ("SOURCE", "TARGET"):
            name = os.environ.get(f"E2E_{role}_SEARCH_SERVICE")
            key = os.environ.get(f"E2E_{role}_SEARCH_ADMIN_KEY")
            if not name or not key:
                self.fail(f"Set E2E_{role}_SEARCH_SERVICE and E2E_{role}_SEARCH_ADMIN_KEY.")
            self.services[role] = (name, key)
        self.binaries = (
            Path(__file__).resolve().parents[1]
            / "AzureSearchBackupRestoreIndex" / "bin" / "Debug" / "net8.0"
        )
        self.assertTrue(
            (self.binaries / "AzureSearchBackupRestoreIndex.dll").exists(),
            "Build the v11 project before running this live test.",
        )
        self.temporary = tempfile.TemporaryDirectory(prefix="v11-roundtrip-e2e-")
        self.addCleanup(self.temporary.cleanup)
        self.owned = []
        self.addCleanup(self.cleanup_indexes)
        prefix = "v11-e2e-" + uuid.uuid4().hex[:16]
        self.source = prefix + "-source"
        self.target = prefix + "-target"

    def request(self, role, path, method="GET", body=None):
        name, key = self.services[role]
        uri = f"https://{name}.search.windows.net/{path}?api-version={API_VERSION}"
        data = None if body is None else json.dumps(body).encode("utf-8")
        request = urllib.request.Request(
            uri, data=data, method=method,
            headers={"api-key": key, "Content-Type": "application/json"},
        )
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                content = response.read()
                return json.loads(content) if content else None
        except urllib.error.HTTPError as error:
            detail = error.read().decode("utf-8", errors="replace")
            for service_name, service_key in self.services.values():
                detail = detail.replace(service_key, "[secret]").replace(service_name, "[search-service]")
            custom_uri = os.environ.get("E2E_CUSTOM_VECTORIZER_URI")
            if custom_uri:
                detail = detail.replace(custom_uri, "[custom-vectorizer-uri]")
            error.msg = f"{error.msg}: {detail}"
            raise

    def index_exists(self, role, name):
        try:
            self.request(role, "indexes/" + name)
            return True
        except urllib.error.HTTPError as error:
            if error.code == 404:
                return False
            raise

    def create_index(self, role, name, document_id, schema=None):
        self.assertFalse(self.index_exists(role, name), "Refusing to overwrite an existing index.")
        schema = schema or {
            "name": name,
            "fields": [{"name": "id", "type": "Edm.String", "key": True}],
        }
        try:
            self.request(role, "indexes", "POST", schema)
        except urllib.error.HTTPError as error:
            if error.code >= 500 and self.index_exists(role, name):
                self.owned.append((role, name))
            raise
        except (urllib.error.URLError, TimeoutError):
            if self.index_exists(role, name):
                self.owned.append((role, name))
            raise
        self.owned.append((role, name))
        response = self.request(
            role, f"indexes/{name}/docs/index", "POST",
            {"value": [{"@search.action": "upload", "id": document_id}]},
        )
        self.assertTrue(all(item["status"] for item in response["value"]))
        deadline = time.monotonic() + 30
        while time.monotonic() < deadline:
            if self.request(role, f"indexes/{name}/docs/$count") == 1:
                return
            time.sleep(1)
        self.fail("Synthetic document was not indexed in time.")

    def cleanup_indexes(self):
        cleaned = []
        for role, name in reversed(self.owned):
            if self.index_exists(role, name):
                self.request(role, "indexes/" + name, "DELETE")
            self.assertFalse(self.index_exists(role, name), "A task-owned index was not deleted.")
            cleaned.append(name)
        print("E2E_RESOURCE_CLEANUP " + json.dumps({"indexes": cleaned, "remainingOwned": 0}))

    def run_sample(self):
        directory = Path(self.temporary.name)
        shutil.copytree(self.binaries, directory, dirs_exist_ok=True)
        backup = directory / "backup"
        backup.mkdir()
        configuration = {
            "SourceSearchServiceName": self.services["SOURCE"][0],
            "SourceAdminKey": self.services["SOURCE"][1],
            "SourceIndexName": self.source,
            "TargetSearchServiceName": self.services["TARGET"][0],
            "TargetAdminKey": self.services["TARGET"][1],
            "TargetIndexName": self.target,
            "BackupDirectory": str(backup),
        }
        configuration_path = directory / "appsettings.json"
        configuration_path.write_text(json.dumps(configuration), encoding="utf-8")
        try:
            result = subprocess.run(
                ["dotnet", str(directory / "AzureSearchBackupRestoreIndex.dll")],
                input="\n\n", capture_output=True, text=True, timeout=180, cwd=directory,
            )
        finally:
            configuration_path.unlink(missing_ok=True)
        return result, backup

    def test_backup_batch_is_valid_json_and_document_is_restored(self):
        self.create_index("SOURCE", self.source, "scalar-fixture")
        self.create_index("TARGET", self.target, "before-restore")
        result, backup = self.run_sample()
        self.assertEqual(0, result.returncode, "The v11 executable failed.")
        batches = list(backup.glob(self.source + "*.json"))
        self.assertEqual(1, len(batches))
        saved = json.loads(batches[0].read_text(encoding="utf-8"))
        self.assertEqual({"value": [{"id": "scalar-fixture"}]}, saved)
        self.assertEqual(1, self.request("TARGET", f"indexes/{self.target}/docs/$count"))
        restored = self.request("TARGET", f"indexes/{self.target}/docs/scalar-fixture")
        self.assertEqual("scalar-fixture", restored["id"])
        target_schema = self.request("TARGET", "indexes/" + self.target)
        self.assertEqual(self.target, target_schema["name"])
        self.assertNotIn("Error:", result.stdout)

    def create_custom_source(self, parameters):
        schema = {
            "name": self.source,
            "fields": [
                {"name": "id", "type": "Edm.String", "key": True},
                {
                    "name": "embedding", "type": "Collection(Edm.Single)",
                    "searchable": True, "dimensions": 2, "vectorSearchProfile": "fixture-profile",
                },
            ],
            "vectorSearch": {
                "algorithms": [{"name": "fixture-hnsw", "kind": "hnsw"}],
                "profiles": [
                    {
                        "name": "fixture-profile", "algorithm": "fixture-hnsw",
                        "vectorizer": "fixture-custom",
                    }
                ],
                "vectorizers": [
                    {
                        "name": "fixture-custom", "kind": "customWebApi",
                        "customWebApiParameters": parameters,
                    }
                ],
            },
        }
        self.create_index("SOURCE", self.source, "scalar-fixture", schema)
        return self.request("SOURCE", "indexes/" + self.source)

    def assert_preflight_preserves_target(self):
        self.create_index("TARGET", self.target, "before-restore")
        before_schema = self.request("TARGET", "indexes/" + self.target)
        before_document = self.request("TARGET", f"indexes/{self.target}/docs/before-restore")
        result, _ = self.run_sample()
        self.assertTrue(self.index_exists("TARGET", self.target), "Preflight deleted the target guard index.")
        self.assertNotEqual(0, result.returncode, "Unsupported redacted custom parameters must fail preflight.")
        self.assertIn("Schema preflight failed", result.stdout + result.stderr)
        self.assertNotIn("Delete target index", result.stdout)
        self.assertEqual(before_schema, self.request("TARGET", "indexes/" + self.target))
        self.assertEqual(
            before_document, self.request("TARGET", f"indexes/{self.target}/docs/before-restore")
        )
        self.assertEqual(1, self.request("TARGET", f"indexes/{self.target}/docs/$count"))

    def test_redacted_custom_header_fails_before_target_is_deleted(self):
        custom_uri = os.environ.get("E2E_CUSTOM_VECTORIZER_URI")
        self.assertTrue(custom_uri, "Set E2E_CUSTOM_VECTORIZER_URI to an existing permitted HTTPS endpoint.")
        source_schema = self.create_custom_source({
            "uri": custom_uri,
            "httpMethod": "POST",
            "httpHeaders": {"X-Fixture-Token": "synthetic-fixture-not-a-real-key"},
        })
        headers = source_schema["vectorSearch"]["vectorizers"][0]["customWebApiParameters"]["httpHeaders"]
        self.assertEqual("<redacted>", headers["X-Fixture-Token"])
        self.assert_preflight_preserves_target()

    def test_redacted_custom_uri_fails_before_target_is_deleted(self):
        custom_uri = os.environ.get("E2E_CUSTOM_VECTORIZER_URI")
        self.assertTrue(custom_uri, "Set E2E_CUSTOM_VECTORIZER_URI to an existing permitted HTTPS endpoint.")
        separator = "&" if "?" in custom_uri else "?"
        source_schema = self.create_custom_source({
            "uri": custom_uri + separator + "code=synthetic-function-key&sig=synthetic-signature",
            "httpMethod": "POST",
        })
        actual_uri = source_schema["vectorSearch"]["vectorizers"][0]["customWebApiParameters"]["uri"]
        self.assertIn("<redacted>", urllib.parse.unquote(actual_uri))
        self.assert_preflight_preserves_target()


if __name__ == "__main__":
    unittest.main()

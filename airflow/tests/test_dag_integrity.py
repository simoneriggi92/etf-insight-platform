import os
import sys

import pytest

# Make DAGs importable from the project root
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", ".."))

from airflow.models import DagBag

DAG_FOLDER = os.path.join(os.path.dirname(__file__), "..", "dags")

EXPECTED_DAGS = {"etf_daily_prices", "etf_backfill_prices", "data_quality_scan"}


@pytest.fixture(scope="module")
def dagbag():
    return DagBag(dag_folder=DAG_FOLDER, include_examples=False)


# -- import health -------------------------------------------------------


def test_no_import_errors(dagbag):
    assert dagbag.import_errors == {}, f"DAG import errors: {dagbag.import_errors}"


# -- presence ------------------------------------------------------------


def test_expected_dags_present(dagbag):
    missing = EXPECTED_DAGS - set(dagbag.dag_ids)
    assert not missing, f"Missing DAGs: {missing}"


# -- etf_daily_prices ----------------------------------------------------


def test_daily_dag_task_ids(dagbag):
    dag = dagbag.dags["etf_daily_prices"]
    task_ids = {t.task_id for t in dag.tasks}
    assert "get_active_symbols" in task_ids
    assert "normalize_and_validate" in task_ids
    assert "load_prices" in task_ids
    assert "trigger_dq_scan" in task_ids


def test_daily_dag_has_fetch_tasks(dagbag):
    dag = dagbag.dags["etf_daily_prices"]
    fetch_tasks = [t for t in dag.tasks if t.task_id.startswith("fetch_prices.")]
    assert len(fetch_tasks) > 0, "No fetch tasks found in fetch_prices TaskGroup"


def test_daily_dag_schedule(dagbag):
    dag = dagbag.dags["etf_daily_prices"]
    assert dag.schedule_interval == "0 22 * * 1-5"
    assert dag.max_active_runs == 1
    assert not dag.catchup


# -- etf_backfill_prices -------------------------------------------------


def test_backfill_dag_has_params(dagbag):
    dag = dagbag.dags["etf_backfill_prices"]
    assert "date_from" in dag.params
    assert "date_to" in dag.params


def test_backfill_dag_task_ids(dagbag):
    dag = dagbag.dags["etf_backfill_prices"]
    task_ids = {t.task_id for t in dag.tasks}
    assert "validate_params" in task_ids
    assert "get_active_symbols" in task_ids
    assert "normalize_and_validate" in task_ids
    assert "load_prices" in task_ids
    assert "trigger_dq_scan" in task_ids


def test_backfill_dag_schedule_is_none(dagbag):
    dag = dagbag.dags["etf_backfill_prices"]
    assert dag.schedule_interval is None
    assert dag.max_active_runs == 1


# -- data_quality_scan ---------------------------------------------------


def test_dq_dag_task_ids(dagbag):
    dag = dagbag.dags["data_quality_scan"]
    task_ids = {t.task_id for t in dag.tasks}
    assert "run_dq_scan" in task_ids


def test_dq_dag_schedule_is_none(dagbag):
    dag = dagbag.dags["data_quality_scan"]
    assert dag.schedule_interval is None
    assert dag.max_active_runs == 3

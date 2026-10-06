"""Consume jobs {videoId, storageKey, exerciseType} published by the API's MinIO webhook.

Status in Postgres: pending -> processing (job picked up) -> done or failed.
"""

import json
import logging
import os
import time

import psycopg
from confluent_kafka import Consumer

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
log = logging.getLogger("pose-worker")


def process_video(job: dict) -> None:
    """Placeholder for MediaPipe pose estimation (later versions)."""
    log.info("process_video stub videoId=%s storageKey=%s exerciseType=%s",
             job.get("videoId"), job.get("storageKey"), job.get("exerciseType"))
    time.sleep(3)  # stand-in for real work so the upload page shows Analyzing


def set_status(conn: psycopg.Connection, video_id: str, new: str, expected: str) -> bool:
    """Move the row from `expected` to `new`; False if it was in any other state."""
    cur = conn.execute(
        """
        UPDATE uploads
        SET status = %s::upload_status, updated_at = now()
        WHERE id = %s AND status = %s::upload_status
        """,
        (new, video_id, expected),
    )
    return cur.rowcount == 1


def handle(conn: psycopg.Connection, job: dict) -> None:
    video_id = job["videoId"]
    # Duplicate or already-handled jobs are skipped.
    if not set_status(conn, video_id, "processing", "pending"):
        log.info("skip videoId=%s (not pending)", video_id)
        return
    log.info("processing videoId=%s", video_id)
    try:
        process_video(job)
    except Exception:
        log.exception("failed videoId=%s", video_id)
        set_status(conn, video_id, "failed", "processing")
        return
    set_status(conn, video_id, "done", "processing")
    log.info("done videoId=%s", video_id)


def main() -> None:
    bootstrap = os.environ["KAFKA_BOOTSTRAP_SERVERS"]
    topic = os.environ.get("KAFKA_TOPIC", "pose-estimation-jobs")
    group_id = os.environ.get("KAFKA_GROUP_ID", "pose-estimation-worker")

    conn = psycopg.connect(
        host=os.environ.get("POSTGRES_HOST", "127.0.0.1"),
        port=os.environ.get("POSTGRES_PORT", "5433"),
        dbname=os.environ["POSTGRES_DB"],
        user=os.environ["POSTGRES_USER"],
        password=os.environ["POSTGRES_PASSWORD"],
        autocommit=True,
    )

    # group.id: Kafka remembers this group's position, so restarts resume where they left off.
    # Auto-commit runs in the background, so a crash mid-job can skip that job.
    consumer = Consumer(
        {
            "bootstrap.servers": bootstrap,
            "group.id": group_id,
            "auto.offset.reset": "earliest",
            "enable.auto.commit": True,
        }
    )
    consumer.subscribe([topic])
    log.info("consuming topic=%s bootstrap=%s", topic, bootstrap)

    try:
        while True:
            msg = consumer.poll(1.0)
            if msg is None:
                continue
            if msg.error():
                log.error("kafka error: %s", msg.error())
                continue
            raw = msg.value().decode("utf-8")
            log.info("received job: %s", raw)
            handle(conn, json.loads(raw))
    finally:
        consumer.close()
        conn.close()


if __name__ == "__main__":
    main()

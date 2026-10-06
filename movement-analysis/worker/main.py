"""Consume jobs {videoId, storageKey, exerciseType} published by the API's MinIO webhook."""

import json
import logging
import os

from confluent_kafka import Consumer

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
log = logging.getLogger("pose-worker")


def process_video(job: dict) -> None:
    """Placeholder for MediaPipe pose estimation (later versions)."""
    log.info("process_video stub videoId=%s storageKey=%s exerciseType=%s",
             job.get("videoId"), job.get("storageKey"), job.get("exerciseType"))


def main() -> None:
    bootstrap = os.environ["KAFKA_BOOTSTRAP_SERVERS"]
    topic = os.environ.get("KAFKA_TOPIC", "pose-estimation-jobs")
    group_id = os.environ.get("KAFKA_GROUP_ID", "pose-estimation-worker")

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
            job = json.loads(raw)
            process_video(job)
    finally:
        consumer.close()


if __name__ == "__main__":
    main()

import signal
import sys
from datetime import datetime


class GracefulKiller:
    """Handle SIGTERM and SIGINT to allow graceful shutdown"""

    def __init__(self):
        self.kill_now = False
        signal.signal(signal.SIGINT, self.exit_gracefully)
        signal.signal(signal.SIGTERM, self.exit_gracefully)

    def exit_gracefully(self, signum, frame):
        """Set shutdown flag when signal received"""
        signal_name = "SIGTERM" if signum == signal.SIGTERM else "SIGINT"
        print(f"\n{'='*60}")
        print(
            f"Received {signal_name} signal at: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}"
        )
        print(f"Received Initiating graceful shutdown...")
        print(f"Waiting for current task to complete...")
        print(f"{'='*60}\n")
        self.kill_now = True

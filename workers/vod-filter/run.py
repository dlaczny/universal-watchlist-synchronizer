"""
VOD Filter - Plex/Radarr/Letterboxd Sync Tool
Standalone executable entry point with colorized console output
"""

import os
import sys
import time
from pathlib import Path

# Add src to path
sys.path.insert(0, str(Path(__file__).parent))

# Enable ANSI color support on Windows
if sys.platform == "win32":
    os.system("color")

try:
    from colorama import init, Fore, Back, Style
    init(autoreset=True)
    HAS_COLOR = True
except ImportError:
    HAS_COLOR = False
    # Fallback: no colors
    class Fore:
        RED = GREEN = YELLOW = BLUE = MAGENTA = CYAN = WHITE = RESET = ""
    class Back:
        RED = GREEN = YELLOW = BLUE = MAGENTA = CYAN = WHITE = RESET = ""
    class Style:
        BRIGHT = DIM = NORMAL = RESET_ALL = ""


def print_header():
    """Print application header."""
    print("\n" + "=" * 80)
    print(Fore.CYAN + Style.BRIGHT + "VOD Filter - Plex/Radarr/Letterboxd Sync Tool")
    print("=" * 80 + Style.RESET_ALL)
    print(f"{Fore.WHITE}Version: 1.0.0")
    print(f"Mode: {'DRY RUN' if '--dry-run' in sys.argv else 'PRODUCTION'}")
    print("=" * 80 + "\n")


def print_step(step_num, description):
    """Print a step header."""
    print(f"\n{Fore.YELLOW}{Style.BRIGHT}[STEP {step_num}] {description}{Style.RESET_ALL}")
    print("-" * 80)


def print_success(message):
    """Print success message."""
    print(f"{Fore.GREEN}✓ {message}{Style.RESET_ALL}")


def print_error(message):
    """Print error message."""
    print(f"{Fore.RED}✗ {message}{Style.RESET_ALL}")


def print_info(message):
    """Print info message."""
    print(f"{Fore.CYAN}ℹ {message}{Style.RESET_ALL}")


def print_warning(message):
    """Print warning message."""
    print(f"{Fore.YELLOW}⚠ {message}{Style.RESET_ALL}")


def setup_logging():
    """Configure structured logging with console output."""
    import structlog

    # Custom renderer for colorized console output
    def colorize_level(logger, method_name, event_dict):
        level = event_dict.get("level", "").upper()

        if level == "ERROR":
            event_dict["level"] = f"{Fore.RED}{level}{Style.RESET_ALL}"
        elif level == "WARNING":
            event_dict["level"] = f"{Fore.YELLOW}{level}{Style.RESET_ALL}"
        elif level == "INFO":
            event_dict["level"] = f"{Fore.GREEN}{level}{Style.RESET_ALL}"
        elif level == "DEBUG":
            event_dict["level"] = f"{Fore.BLUE}{level}{Style.RESET_ALL}"

        return event_dict

    structlog.configure(
        processors=[
            structlog.stdlib.add_log_level,
            structlog.processors.TimeStamper(fmt="%H:%M:%S"),
            colorize_level if HAS_COLOR else structlog.processors.KeyValueRenderer(),
            structlog.dev.ConsoleRenderer(colors=HAS_COLOR),
        ],
        wrapper_class=structlog.stdlib.BoundLogger,
        context_class=dict,
        logger_factory=structlog.PrintLoggerFactory(),
        cache_logger_on_first_use=True,
    )


def main():
    """Main entry point."""
    try:
        print_header()

        # Setup logging
        setup_logging()

        # Check for .env file
        env_path = Path(__file__).parent / ".env"
        if not env_path.exists():
            print_error(f".env file not found at {env_path}")
            print_info("Please create a .env file with your configuration")
            input("\nPress Enter to exit...")
            return 1

        print_success(f"Found .env configuration at {env_path}")

        # Load environment
        from dotenv import load_dotenv
        load_dotenv(env_path)

        # Import main workflow
        from src.main import main as run_workflow

        # Determine if dry run
        dry_run = "--dry-run" in sys.argv

        if dry_run:
            print_warning("Running in DRY RUN mode - no changes will be made")
        else:
            print_info("Running in PRODUCTION mode - changes will be applied")

        print("\n" + "=" * 80)
        print(f"{Fore.CYAN}{Style.BRIGHT}Starting Sync Workflow...{Style.RESET_ALL}")
        print("=" * 80 + "\n")

        # Run the workflow
        start_time = time.time()
        exit_code = run_workflow()
        elapsed_time = time.time() - start_time

        print("\n" + "=" * 80)
        if exit_code == 0:
            print_success(f"Sync completed successfully in {elapsed_time:.1f} seconds")
        else:
            print_error(f"Sync failed with exit code {exit_code}")
        print("=" * 80 + "\n")

        # Keep console open if running from double-click
        if not sys.stdin.isatty():
            input("\nPress Enter to exit...")

        return exit_code

    except KeyboardInterrupt:
        print_warning("\n\nSync interrupted by user")
        return 130
    except Exception as e:
        print_error(f"Fatal error: {e}")
        import traceback
        traceback.print_exc()

        # Keep console open to show error
        if not sys.stdin.isatty():
            input("\nPress Enter to exit...")

        return 1


if __name__ == "__main__":
    sys.exit(main())

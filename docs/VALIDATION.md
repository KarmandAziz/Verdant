# Validation

The source snapshot passed 43 core checks and 10 hardware-coordinator checks before public packaging.

Core checks cover held-action ownership, toggle release, shared inputs, repeat cancellation, configuration validation, extra-key scheduling, and balanced hardware keyboard encoding. Coordinator checks use a fake backend to cover activation, OFF, pause, profile changes, cleanup failures, and rearming.

Interactive native checks on a USB Basilisk V3 verified:

- Continued mouse hold alongside repeated hardware F presses, with mouse movement during the test.
- The second physical click stopping both inputs, followed by an observation period with no further F.
- Pause stopping both inputs.
- Forced application termination followed by guardian recovery, released input state, removed recovery lease, and no further raw F during a four-second observation.

Manual application testing also confirmed the combined held mouse action, continued extra-key behavior after release, and second-click cancellation on the tested setup.

These results are limited to that Windows setup and mouse. They do not establish compatibility with every application, mouse firmware, rapid input sequence, device disconnect, or operating-system failure.

Private test reports, screenshots, saved configurations, and device captures are intentionally excluded from this repository.

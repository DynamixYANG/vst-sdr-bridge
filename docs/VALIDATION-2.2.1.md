# VST Bridge 2.2.1 validation

Patch regression on 1 October 2026, Windows x64 / NI PXIe-5644R RIO0. Shipped identities:

| Binary | SHA-256 |
|---|---|
| VSTHub.exe | `6b5a21ff376676317e6246003909973d382b823d792ff248febca6976d4bf392` |
| vstSupport.dll | `63e2bae5fdeb8fc45cc117f70e97493889e4999b2430b5c16ad512278234c40a` |

39 self-checks PASS. Real cold startup passes seven control checks: RX/TX stopped, RF off, RX FIFO unallocated, no IQ publication, idle configuration stays idle, TX alone before the first RX start, explicit duplex and independent stops. Native Soapy passes eight checks: construction/configuration/setup stay stopped; inactive getters retain 30.72 MS/s / 2500 MHz / -20 dBm; activation applies the staged settings and delivers real CS16 IQ.

Twelve actual WinForms widget renders cover six pages at default/minimum size, 144 DPI. Four RX/TX configuration images were visually inspected. All visible unfocused numeric fields report native SelectionLength=0. They retain dark backgrounds/white text. These are widget-render checks, not desktop click automation.

The ordinary generated GRC main() starts from RX STOPPED and runs duplex for 120.094 seconds. RX DMA/delivered 120.000992 MS/s; TX processed 120.001120 MS/s. No sink TIMEOUT, underflow, active RX loss, display skip, overflow, recovery or logger errors. 5 actual Qt application captures are preserved. RF output is off after close.

Development attempts are retained: initial sink TIMEOUT; stopped-configuration parse failure; one TIMEOUT before TX initialization ordering was corrected. Merely exposing the live ring before DMA/RFSG configuration finished allowed a fast client to fill it and exceed a writeStream timeout. The fix configures hardware before exposing the ring; steady streaming logic is unchanged. This is a bounded regression, not proof that arbitrary machine load can never produce a timeout.

The native plugin now sends START on RX activation and supports staged settings through parseable CONFIG/CONFIG2 replies. Before activation, ordinary Soapy getters return requested settings; actual hardware sensors retain hardware readback. CONFIG2 pending=1 identifies staged values. The Hub never starts acquisition merely because the application or client device is opened.

The original [GRC 20-minute and user-accepted TDMS/GQRX ~17-minute evidence](VALIDATION.md) belongs to earlier 2.2 binaries. Both Hub and native plugin changed in this patch; those long runs are not represented as new-binary acceptance. The operator had cancelled TDMS/GQRX repetition. No new long-duration acceptance was requested for this patch.

Evidence: tests/artifacts/startup-2.2.1, soapy-idle-2.2.1, ui-idle-2.2.1, grc-startup-2.2.1-clientactivation and release-review-2.2.1.json.

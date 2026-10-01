#!/usr/bin/env python3
# -*- coding: utf-8 -*-

#
# SPDX-License-Identifier: GPL-3.0
#
# GNU Radio Python Flow Graph
# Title: VST Bridge 2.2.0 - 120 MS/s duplex
# Author: MagicYang
# Description: 120 MS/s TX/RX antenna-coupled +1 MHz tone. RF enabled, peak -10 dBm, RX reference -20 dBm. Stop flowgraph to turn RF off.
# GNU Radio version: 3.10.12.0

from PyQt5 import Qt
from gnuradio import qtgui
from gnuradio import blocks
from gnuradio import gr
from gnuradio.filter import firdes
from gnuradio.fft import window
import sys
import signal
from PyQt5 import Qt
from argparse import ArgumentParser
from gnuradio.eng_arg import eng_float, intx
from gnuradio import eng_notation
from gnuradio import soapy
import sip
import threading


def snipfcn_tx_scheduler(self):
    self.analog_sig_source_x_0.set_min_noutput_items(262144)
    self.analog_sig_source_x_0.set_max_noutput_items(262144)
    self.soapy_custom_sink_0.set_min_noutput_items(262144)
    self.soapy_custom_sink_0.set_max_noutput_items(262144)
    # Render the Qt window before starting time-critical RF streaming.
    self.resize(1400, 900)
    self.show()
    Qt.QApplication.processEvents()
    print("VST Bridge example 2.2.0: TX scheduler 262144; RX/TX buffers 1048576", flush=True)
    # Optional observational captures only; normal GRC startup uses the same main().
    import os
    capture_dir = os.environ.get("VST_GRC_CAPTURE_DIR")
    if capture_dir:
        import pathlib
        output = pathlib.Path(capture_dir)
        output.mkdir(parents=True, exist_ok=True)
        self._capture_index = 0
        def capture():
            self.grab().save(str(output / ("grc-render-%04d.png" % self._capture_index)))
            self._capture_index += 1
            if (output / "close.request").exists():
                self.close()
        self._capture_timer = Qt.QTimer(self)
        self._capture_timer.timeout.connect(capture)
        self._capture_timer.start(30000)


def snippets_main_after_init(tb):
    snipfcn_tx_scheduler(tb)

class vst_bridge_120_duplex(gr.top_block, Qt.QWidget):

    def __init__(self):
        gr.top_block.__init__(self, "VST Bridge 2.2.0 - 120 MS/s duplex", catch_exceptions=True)
        Qt.QWidget.__init__(self)
        self.setWindowTitle("VST Bridge 2.2.0 - 120 MS/s duplex")
        qtgui.util.check_set_qss()
        try:
            self.setWindowIcon(Qt.QIcon.fromTheme('gnuradio-grc'))
        except BaseException as exc:
            print(f"Qt GUI: Could not set Icon: {str(exc)}", file=sys.stderr)
        self.top_scroll_layout = Qt.QVBoxLayout()
        self.setLayout(self.top_scroll_layout)
        self.top_scroll = Qt.QScrollArea()
        self.top_scroll.setFrameStyle(Qt.QFrame.NoFrame)
        self.top_scroll_layout.addWidget(self.top_scroll)
        self.top_scroll.setWidgetResizable(True)
        self.top_widget = Qt.QWidget()
        self.top_scroll.setWidget(self.top_widget)
        self.top_layout = Qt.QVBoxLayout(self.top_widget)
        self.top_grid_layout = Qt.QGridLayout()
        self.top_layout.addLayout(self.top_grid_layout)

        self.settings = Qt.QSettings("gnuradio/flowgraphs", "vst_bridge_120_duplex")

        try:
            geometry = self.settings.value("geometry")
            if geometry:
                self.restoreGeometry(geometry)
        except BaseException as exc:
            print(f"Qt GUI: Could not restore geometry: {str(exc)}", file=sys.stderr)
        self.flowgraph_started = threading.Event()

        ##################################################
        # Variables
        ##################################################
        self.vec_len = vec_len = 262144
        self.tx_peak_dbm = tx_peak_dbm = -10
        self.tone_hz = tone_hz = 50e6
        self.samp_rate = samp_rate = 120e6
        self.rx_ref_dbm = rx_ref_dbm = -20
        self.freq = freq = 2.5e9

        ##################################################
        # Blocks
        ##################################################

        self.soapy_custom_source_0 = None
        dev = 'driver=' + 'vst'
        stream_args = ''
        tune_args = ['']
        settings = ['']
        self.soapy_custom_source_0 = soapy.source(dev, "fc32",
                                  1, 'resource=RIO0,backend=dma,transport=shm,display_stride=1,display_mode=snapshot,report_rate=true,max_out=65536',
                                  stream_args, tune_args, settings)
        self.soapy_custom_source_0.set_sample_rate(0, samp_rate)
        self.soapy_custom_source_0.set_bandwidth(0, 0)
        self.soapy_custom_source_0.set_antenna(0, 'RF_IN')
        self.soapy_custom_source_0.set_frequency(0, freq)
        self.soapy_custom_source_0.set_frequency_correction(0, 0)
        self.soapy_custom_source_0.set_gain_mode(0, False)
        self.soapy_custom_source_0.set_gain(0, rx_ref_dbm)
        self.soapy_custom_source_0.set_dc_offset_mode(0, False)
        self.soapy_custom_source_0.set_dc_offset(0, 0)
        self.soapy_custom_source_0.set_iq_balance(0, 0)
        self.soapy_custom_source_0.set_min_output_buffer(1048576)
        self.soapy_custom_sink_0 = None
        dev = 'driver=' + 'vst'
        stream_args = ''
        tune_args = ['']
        settings = ["rf_enabled=true"]
        self.soapy_custom_sink_0 = soapy.sink(dev, "fc32",
                                1, 'resource=RIO0,rf_enabled=true,peak_dbm=-10',
                                stream_args, tune_args, settings)
        self.soapy_custom_sink_0.set_sample_rate(0, samp_rate)
        self.soapy_custom_sink_0.set_bandwidth(0, 0)
        self.soapy_custom_sink_0.set_antenna(0, 'RF_OUT')
        self.soapy_custom_sink_0.set_frequency(0, freq)
        self.soapy_custom_sink_0.set_frequency_correction(0, 0)
        self.soapy_custom_sink_0.set_gain(0, tx_peak_dbm)
        self.soapy_custom_sink_0.set_dc_offset(0, 0)
        self.soapy_custom_sink_0.set_iq_balance(0, 0)
        self.qtgui_waterfall_sink_x_0 = qtgui.waterfall_sink_c(
            4096, #size
            window.WIN_BLACKMAN_hARRIS, #wintype
            freq, #fc
            samp_rate, #bw
            "RX Waterfall", #name
            1, #number of inputs
            None # parent
        )
        self.qtgui_waterfall_sink_x_0.set_update_time(0.10)
        self.qtgui_waterfall_sink_x_0.enable_grid(False)
        self.qtgui_waterfall_sink_x_0.enable_axis_labels(True)



        labels = ['', '', '', '', '',
                  '', '', '', '', '']
        colors = [0, 0, 0, 0, 0,
                  0, 0, 0, 0, 0]
        alphas = [1.0, 1.0, 1.0, 1.0, 1.0,
                  1.0, 1.0, 1.0, 1.0, 1.0]

        for i in range(1):
            if len(labels[i]) == 0:
                self.qtgui_waterfall_sink_x_0.set_line_label(i, "Data {0}".format(i))
            else:
                self.qtgui_waterfall_sink_x_0.set_line_label(i, labels[i])
            self.qtgui_waterfall_sink_x_0.set_color_map(i, colors[i])
            self.qtgui_waterfall_sink_x_0.set_line_alpha(i, alphas[i])

        self.qtgui_waterfall_sink_x_0.set_intensity_range(-140, 10)

        self._qtgui_waterfall_sink_x_0_win = sip.wrapinstance(self.qtgui_waterfall_sink_x_0.qwidget(), Qt.QWidget)

        self.top_grid_layout.addWidget(self._qtgui_waterfall_sink_x_0_win, 1, 0, 1, 1)
        for r in range(1, 2):
            self.top_grid_layout.setRowStretch(r, 1)
        for c in range(0, 1):
            self.top_grid_layout.setColumnStretch(c, 1)
        self.qtgui_freq_sink_x_0 = qtgui.freq_sink_c(
            4096, #size
            window.WIN_BLACKMAN_hARRIS, #wintype
            freq, #fc
            samp_rate, #bw
            "RX Spectrum CF=2500 MHz (tone @ +1 MHz)", #name
            1,
            None # parent
        )
        self.qtgui_freq_sink_x_0.set_update_time(0.10)
        self.qtgui_freq_sink_x_0.set_y_axis((-140), 10)
        self.qtgui_freq_sink_x_0.set_y_label('Relative Gain', "")
        self.qtgui_freq_sink_x_0.set_trigger_mode(qtgui.TRIG_MODE_FREE, 0.0, 0, "")
        self.qtgui_freq_sink_x_0.enable_autoscale(False)
        self.qtgui_freq_sink_x_0.enable_grid(True)
        self.qtgui_freq_sink_x_0.set_fft_average(0.2)
        self.qtgui_freq_sink_x_0.enable_axis_labels(True)
        self.qtgui_freq_sink_x_0.enable_control_panel(False)
        self.qtgui_freq_sink_x_0.set_fft_window_normalized(False)



        labels = ['', '', '', '', '',
            '', '', '', '', '']
        widths = [1, 1, 1, 1, 1,
            1, 1, 1, 1, 1]
        colors = ["blue", "red", "green", "black", "cyan",
            "magenta", "yellow", "dark red", "dark green", "dark blue"]
        alphas = [1.0, 1.0, 1.0, 1.0, 1.0,
            1.0, 1.0, 1.0, 1.0, 1.0]

        for i in range(1):
            if len(labels[i]) == 0:
                self.qtgui_freq_sink_x_0.set_line_label(i, "Data {0}".format(i))
            else:
                self.qtgui_freq_sink_x_0.set_line_label(i, labels[i])
            self.qtgui_freq_sink_x_0.set_line_width(i, widths[i])
            self.qtgui_freq_sink_x_0.set_line_color(i, colors[i])
            self.qtgui_freq_sink_x_0.set_line_alpha(i, alphas[i])

        self._qtgui_freq_sink_x_0_win = sip.wrapinstance(self.qtgui_freq_sink_x_0.qwidget(), Qt.QWidget)
        self.top_grid_layout.addWidget(self._qtgui_freq_sink_x_0_win, 0, 0, 1, 1)
        for r in range(0, 1):
            self.top_grid_layout.setRowStretch(r, 1)
        for c in range(0, 1):
            self.top_grid_layout.setColumnStretch(c, 1)
        self.analog_sig_source_x_0 = blocks.vector_source_c([0.5*complex(__import__('math').cos(2*__import__('math').pi*k/120),__import__('math').sin(2*__import__('math').pi*k/120)) for k in range(120)]*2184, True, 1, [])
        self.analog_sig_source_x_0.set_min_output_buffer(1048576)


        ##################################################
        # Connections
        ##################################################
        self.connect((self.analog_sig_source_x_0, 0), (self.soapy_custom_sink_0, 0))
        self.connect((self.soapy_custom_source_0, 0), (self.qtgui_freq_sink_x_0, 0))
        self.connect((self.soapy_custom_source_0, 0), (self.qtgui_waterfall_sink_x_0, 0))


    def closeEvent(self, event):
        self.settings = Qt.QSettings("gnuradio/flowgraphs", "vst_bridge_120_duplex")
        self.settings.setValue("geometry", self.saveGeometry())
        self.stop()
        self.wait()

        event.accept()

    def get_vec_len(self):
        return self.vec_len

    def set_vec_len(self, vec_len):
        self.vec_len = vec_len

    def get_tx_peak_dbm(self):
        return self.tx_peak_dbm

    def set_tx_peak_dbm(self, tx_peak_dbm):
        self.tx_peak_dbm = tx_peak_dbm
        self.soapy_custom_sink_0.set_gain(0, self.tx_peak_dbm)

    def get_tone_hz(self):
        return self.tone_hz

    def set_tone_hz(self, tone_hz):
        self.tone_hz = tone_hz

    def get_samp_rate(self):
        return self.samp_rate

    def set_samp_rate(self, samp_rate):
        self.samp_rate = samp_rate
        self.qtgui_freq_sink_x_0.set_frequency_range(self.freq, self.samp_rate)
        self.qtgui_waterfall_sink_x_0.set_frequency_range(self.freq, self.samp_rate)

    def get_rx_ref_dbm(self):
        return self.rx_ref_dbm

    def set_rx_ref_dbm(self, rx_ref_dbm):
        self.rx_ref_dbm = rx_ref_dbm
        self.soapy_custom_source_0.set_gain(0, self.rx_ref_dbm)

    def get_freq(self):
        return self.freq

    def set_freq(self, freq):
        self.freq = freq
        self.qtgui_freq_sink_x_0.set_frequency_range(self.freq, self.samp_rate)
        self.qtgui_waterfall_sink_x_0.set_frequency_range(self.freq, self.samp_rate)
        self.soapy_custom_sink_0.set_frequency(0, self.freq)
        self.soapy_custom_source_0.set_frequency(0, self.freq)




def main(top_block_cls=vst_bridge_120_duplex, options=None):

    qapp = Qt.QApplication(sys.argv)

    tb = top_block_cls()
    snippets_main_after_init(tb)
    tb.start()
    tb.flowgraph_started.set()

    tb.show()

    def sig_handler(sig=None, frame=None):
        tb.stop()
        tb.wait()

        Qt.QApplication.quit()

    signal.signal(signal.SIGINT, sig_handler)
    signal.signal(signal.SIGTERM, sig_handler)

    timer = Qt.QTimer()
    timer.start(500)
    timer.timeout.connect(lambda: None)

    qapp.exec_()

if __name__ == '__main__':
    main()

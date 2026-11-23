const formats = new Set(['ean_13', 'ean_8', 'upc_a', 'upc_e']);
let stream;
let timer;

export async function start(video, page, zxingModuleUrl, zxingWasmUrl) {
    if (!navigator.mediaDevices?.getUserMedia) {
        return 'unsupported';
    }
    let detector;
    try {
        detector = await createDetector(zxingModuleUrl, zxingWasmUrl);
    } catch {
        return 'unsupported';
    }
    try {
        stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' }, audio: false });
    } catch {
        return 'denied';
    }
    video.srcObject = stream;
    await video.play();

    const scan = async () => {
        if (!stream) {
            return;
        }
        try {
            const codes = await detector.detect(video);
            if (codes.length > 0) {
                stop();
                await page.invokeMethodAsync('OnDetected', codes[0].rawValue);
                return;
            }
        } catch {
            // A frame that isn't ready yet; the next one will be.
        }
        timer = setTimeout(scan, 250);
    };
    void scan();
    return 'started';
}

// Chrome on Android has a built-in detector; Firefox has none, and Chrome on Windows has one without any formats.
async function createDetector(zxingModuleUrl, zxingWasmUrl) {
    if ('BarcodeDetector' in window) {
        const supported = (await BarcodeDetector.getSupportedFormats()).filter(f => formats.has(f));
        if (supported.length > 0) {
            return new BarcodeDetector({ formats: supported });
        }
    }
    // Relative URLs would resolve against this module's folder, not the app's base.
    const wasm = new URL(zxingWasmUrl, document.baseURI).href;
    const zxing = await import(new URL(zxingModuleUrl, document.baseURI).href);
    // Without locateFile the library fetches the .wasm from jsDelivr. Loading it now, not on the first frame,
    // turns a failed download into "unsupported" instead of a camera that never scans.
    await zxing.prepareZXingModule({
        overrides: { locateFile: (path, prefix) => path.endsWith('.wasm') ? wasm : prefix + path },
        fireImmediately: true
    });
    return new zxing.BarcodeDetector({ formats: [...formats] });
}

export function stop() {
    clearTimeout(timer);
    stream?.getTracks().forEach(track => track.stop());
    stream = undefined;
}

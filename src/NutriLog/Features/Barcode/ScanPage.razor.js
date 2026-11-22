const formats = new Set(['ean_13', 'ean_8', 'upc_a', 'upc_e']);
let stream;
let timer;

export async function start(video, page) {
    if (!('BarcodeDetector' in window) || !navigator.mediaDevices?.getUserMedia) {
        return 'unsupported';
    }
    const supported = (await BarcodeDetector.getSupportedFormats()).filter(f => formats.has(f));
    if (supported.length === 0) {
        return 'unsupported';
    }
    const detector = new BarcodeDetector({ formats: supported });
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

export function stop() {
    clearTimeout(timer);
    stream?.getTracks().forEach(track => track.stop());
    stream = undefined;
}

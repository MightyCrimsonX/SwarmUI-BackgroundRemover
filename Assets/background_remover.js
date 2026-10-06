/**
 * character_rembg.js
 * Frontend controller for SwarmUI Character Background Remover extension.
 * Provides interactive character segmentation, image comparison, and quick-action buttons.
 */

'use strict';

class CharacterRemBgHelper {
    constructor() {
        this.isProcessing = false;
        this.modalElement = null;
        this.lastOriginalSrc = null;
        this.lastProcessedBase64 = null;
    }

    /**
     * Converts an image source URL or data URI into base64 string.
     * @param {string} src - The image URL or data URI.
     */
    async getImageBase64(src) {
        if (!src) {
            return null;
        }
        if (src.startsWith('data:')) {
            let commaIndex = src.indexOf(',');
            if (commaIndex >= 0) {
                return src.substring(commaIndex + 1);
            }
            return src;
        }
        let response = await fetch(src);
        let blob = await response.blob();
        return new Promise((resolve, reject) => {
            let reader = new FileReader();
            reader.onloadend = () => {
                let res = reader.result;
                let comma = res.indexOf(',');
                if (comma >= 0) {
                    resolve(res.substring(comma + 1));
                }
                else {
                    resolve(res);
                }
            };
            reader.onerror = () => {
                reject(new Error('Failed to load image for conversion.'));
            };
            reader.readAsDataURL(blob);
        });
    }

    /**
     * Gathers currently selected parameter options from the left sidebar controls.
     */
        let getEl = (name) => document.getElementById('input_backgroundremover' + name) || document.getElementById('input_characterrembg' + name);

        let engine = 'yolov8_person';
        let engineInput = getEl('engine');
        if (engineInput && typeof getInputVal == 'function') {
            engine = getInputVal(engineInput) || engine;
        }

        let yoloModel = 'yolov8m-seg.pt';
        let yoloInput = getEl('yolomodel');
        if (yoloInput && typeof getInputVal == 'function') {
            yoloModel = getInputVal(yoloInput) || yoloModel;
        }

        let confidence = 0.30;
        let confInput = getEl('confidencethreshold');
        if (confInput && typeof getInputVal == 'function') {
            let val = parseFloat(getInputVal(confInput));
            if (!isNaN(val)) {
                confidence = val;
            }
        }

        let personSelection = 'all';
        let personInput = getEl('subjectselection');
        if (personInput && typeof getInputVal == 'function') {
            personSelection = getInputVal(personInput) || personSelection;
        }

        let maskPadding = 0;
        let padInput = getEl('maskedgepadding');
        if (padInput && typeof getInputVal == 'function') {
            let val = parseInt(getInputVal(padInput), 10);
            if (!isNaN(val)) {
                maskPadding = val;
            }
        }

        let maskBlur = 2;
        let blurInput = getEl('maskfeatheringblur');
        if (blurInput && typeof getInputVal == 'function') {
            let val = parseInt(getInputVal(blurInput), 10);
            if (!isNaN(val)) {
                maskBlur = val;
            }
        }

        let backgroundColor = 'transparent';
        let bgInput = getEl('outputbackground');
        if (bgInput && typeof getInputVal == 'function') {
            backgroundColor = getInputVal(bgInput) || backgroundColor;
        }

        let customColor = '#FFFFFF';
        let customInput = getEl('custombackgroundcolor');
        if (customInput && typeof getInputVal == 'function') {
            customColor = getInputVal(customInput) || customColor;
        }

        let fillHoles = true;
        let fillInput = getEl('fillinteriorholes');
        if (fillInput && typeof getInputVal == 'function') {
            fillHoles = getInputVal(fillInput) == 'true';
        }

        let invertMask = false;
        let invertInput = getEl('invertmask');
        if (invertInput && typeof getInputVal == 'function') {
            invertMask = getInputVal(invertInput) == 'true';
        }

        return {
            engine: engine,
            yoloModel: yoloModel,
            confidence: confidence,
            personSelection: personSelection,
            maskPadding: maskPadding,
            maskBlur: maskBlur,
            backgroundColor: backgroundColor,
            customColor: customColor,
            fillHoles: fillHoles,
            invertMask: invertMask
        };
    }

    /**
     * Builds and appends the comparison result modal to the DOM.
     */
    buildModal() {
        if (this.modalElement) {
            return this.modalElement;
        }

        let modal = document.createElement('div');
        modal.className = 'character-rembg-modal';

        let panel = document.createElement('div');
        panel.className = 'character-rembg-modal-panel';

        let header = document.createElement('div');
        header.className = 'character-rembg-modal-header';

        let title = document.createElement('h3');
        title.className = 'character-rembg-modal-title';
        title.textContent = 'Character Background Removal Result';

        let closeBtn = document.createElement('button');
        closeBtn.type = 'button';
        closeBtn.className = 'character-rembg-modal-close';
        closeBtn.innerHTML = '&times;';
        closeBtn.onclick = () => {
            modal.classList.remove('visible');
        };

        header.appendChild(title);
        header.appendChild(closeBtn);

        let body = document.createElement('div');
        body.className = 'character-rembg-modal-body';

        let statusBar = document.createElement('div');
        statusBar.className = 'character-rembg-status-bar';
        statusBar.textContent = 'Background successfully removed.';

        let grid = document.createElement('div');
        grid.className = 'character-rembg-comparison-grid';

        // Original Card
        let origCard = document.createElement('div');
        origCard.className = 'character-rembg-card';
        let origHeader = document.createElement('div');
        origHeader.className = 'character-rembg-card-header';
        origHeader.textContent = 'Original Input';
        let origBody = document.createElement('div');
        origBody.className = 'character-rembg-card-body';
        let origImg = document.createElement('img');
        origImg.className = 'character-rembg-preview-img character-rembg-orig-img';
        origBody.appendChild(origImg);
        origCard.appendChild(origHeader);
        origCard.appendChild(origBody);

        // Processed Card
        let procCard = document.createElement('div');
        procCard.className = 'character-rembg-card';
        let procHeader = document.createElement('div');
        procHeader.className = 'character-rembg-card-header';
        procHeader.textContent = 'Isolated Character (Transparent)';
        let procBody = document.createElement('div');
        procBody.className = 'character-rembg-card-body character-rembg-checkerboard';
        let procImg = document.createElement('img');
        procImg.className = 'character-rembg-preview-img character-rembg-proc-img';
        procBody.appendChild(procImg);
        procCard.appendChild(procHeader);
        procCard.appendChild(procBody);

        grid.appendChild(origCard);
        grid.appendChild(procCard);

        body.appendChild(statusBar);
        body.appendChild(grid);

        let actions = document.createElement('div');
        actions.className = 'character-rembg-modal-actions';

        let downloadBtn = document.createElement('button');
        downloadBtn.type = 'button';
        downloadBtn.className = 'character-rembg-btn-primary';
        downloadBtn.textContent = 'Download PNG';
        downloadBtn.onclick = () => {
            if (this.lastProcessedBase64) {
                let link = document.createElement('a');
                link.href = 'data:image/png;base64,' + this.lastProcessedBase64;
                link.download = 'character_isolated_' + Date.now() + '.png';
                link.click();
            }
        };

        let useInitBtn = document.createElement('button');
        useInitBtn.type = 'button';
        useInitBtn.className = 'character-rembg-btn-secondary';
        useInitBtn.textContent = 'Use as Init Image';
        useInitBtn.onclick = () => {
            if (this.lastProcessedBase64) {
                let dataUri = 'data:image/png;base64,' + this.lastProcessedBase64;
                if (typeof setMediaFileInput == 'function') {
                    let initElem = document.getElementById('input_initimage');
                    if (initElem) {
                        setMediaFileInput(initElem, dataUri, 'character.png');
                    }
                }
                modal.classList.remove('visible');
            }
        };

        let dismissBtn = document.createElement('button');
        dismissBtn.type = 'button';
        dismissBtn.className = 'character-rembg-btn-secondary';
        dismissBtn.textContent = 'Close';
        dismissBtn.onclick = () => {
            modal.classList.remove('visible');
        };

        actions.appendChild(useInitBtn);
        actions.appendChild(downloadBtn);
        actions.appendChild(dismissBtn);

        panel.appendChild(header);
        panel.appendChild(body);
        panel.appendChild(actions);
        modal.appendChild(panel);

        document.body.appendChild(modal);
        this.modalElement = modal;
        return modal;
    }

    /**
     * Executes background removal on the given image source.
     * @param {string} src - The image URL or data URI.
     */
    async processImageSource(src) {
        if (!src) {
            if (typeof showError == 'function') {
                showError('Character Background Remover: No image specified.');
            }
            return;
        }

        if (this.isProcessing) {
            return;
        }

        this.isProcessing = true;
        let base64 = null;

        try {
            base64 = await this.getImageBase64(src);
        }
        catch (err) {
            this.isProcessing = false;
            if (typeof showError == 'function') {
                showError('Character Background Remover: Failed to read image data.');
            }
            return;
        }

        let settings = this.gatherCurrentSettings();
        let payload = {
            imageBase64: base64,
            engine: settings.engine,
            yoloModel: settings.yoloModel,
            confidence: settings.confidence,
            personSelection: settings.personSelection,
            maskPadding: settings.maskPadding,
            maskBlur: settings.maskBlur,
            backgroundColor: settings.backgroundColor,
            customColor: settings.customColor,
            fillHoles: settings.fillHoles,
            invertMask: settings.invertMask
        };

        if (typeof genericRequest == 'function') {
            genericRequest('RemoveCharacterBackground', payload, (response) => {
                this.isProcessing = false;
                if (!response || !response.success || !response.image) {
                    let errMsg = (response && response.error) ? response.error : 'Background removal failed.';
                    if (typeof showError == 'function') {
                        showError('Character Background Remover: ' + errMsg);
                    }
                    return;
                }

                this.lastOriginalSrc = src;
                this.lastProcessedBase64 = response.image;

                let modal = this.buildModal();
                let origImg = modal.querySelector('.character-rembg-orig-img');
                let procImg = modal.querySelector('.character-rembg-proc-img');
                let statusBar = modal.querySelector('.character-rembg-status-bar');

                if (origImg) {
                    origImg.src = src;
                }
                if (procImg) {
                    procImg.src = 'data:image/png;base64,' + response.image;
                }
                if (statusBar) {
                    statusBar.textContent = 'Character successfully isolated using ' + settings.engine + ' (' + settings.yoloModel + ').';
                }

                modal.classList.add('visible');
            }, 0, (err) => {
                this.isProcessing = false;
                if (typeof showError == 'function') {
                    showError('Character Background Remover error: ' + err);
                }
            });
        }
        else {
            this.isProcessing = false;
        }
    }

    /**
     * Injects an action button into the [Character RemBg] Input Image parameter box in the left sidebar.
     */
        let inputElem = document.getElementById('input_backgroundremoverinputimage') || document.getElementById('input_characterrembginputimage');
        if (!inputElem) {
            return;
        }

        let parent = inputElem.closest('.auto-file-box') || inputElem.parentElement;
        if (!parent || parent.querySelector('.character-rembg-action-btn')) {
            return;
        }

        let btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'character-rembg-action-btn';
        btn.textContent = 'Remove Background from Image';
        btn.title = 'Isolate the character and remove background using current parameters';

        btn.onclick = () => {
            let fileData = null;
            if (typeof getInputVal == 'function') {
                fileData = getInputVal(inputElem);
            }
            if (!fileData) {
                let previewImg = parent.querySelector('.auto-input-preview img');
                if (previewImg && previewImg.src) {
                    fileData = previewImg.src;
                }
            }
            if (!fileData) {
                if (typeof showError == 'function') {
                    showError('Character Background Remover: Please upload or select an image in [Character RemBg] Input Image first.');
                }
                return;
            }
            this.processImageSource(fileData);
        };

        parent.appendChild(btn);
    }
}

/** Global instance of the character background removal helper. */
let characterRemBgHelper = new CharacterRemBgHelper();

setTimeout(() => {
    if (typeof registerMediaButton == 'function') {
        registerMediaButton(
            'Remove Background (Character Only)',
            (src) => characterRemBgHelper.processImageSource(src),
            'Isolate character and remove background using YOLO segmentation',
            ['image'],
            false,
            true
        );
    }
}, 50);

if (typeof postParamBuildSteps !== 'undefined') {
    postParamBuildSteps.push(() => characterRemBgHelper.initParameterButton());
}
setTimeout(() => characterRemBgHelper.initParameterButton(), 250);
setTimeout(() => characterRemBgHelper.initParameterButton(), 1000);

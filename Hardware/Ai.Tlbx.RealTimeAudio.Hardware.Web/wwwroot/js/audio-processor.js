// audio-processor.js
class AudioRecorderProcessor extends AudioWorkletProcessor {
    constructor() {
        super();
        // Create a buffer to store audio data (4096 samples)
        this.buffer = new Int16Array(4096);
        this.bufferIndex = 0;
        this.isActive = true;
        
        // Listen for messages from the main thread
        this.port.onmessage = (event) => {
            if (event.data?.command === 'stop') {
                console.log("[AudioProcessor] Received stop command");
                this.isActive = false;
            }
        };
    }

    process(inputs, outputs) {
        // If we've been told to stop, return false to terminate the processor
        if (!this.isActive) {
            console.log("[AudioProcessor] Stopping processor");
            return false;
        }
        
        // Get the first channel of the first input
        const input = inputs[0]?.[0];
        
        if (!input || input.length === 0) {
            // No input data, continue processing
            return true;
        }

        // Process the audio data
        for (let i = 0; i < input.length; i++) {
            // Convert float [-1, 1] to 16-bit PCM [-32768, 32767]
            const sample = Math.max(-1, Math.min(1, input[i]));
            const pcmValue = sample < 0 ? sample * 32768 : sample * 32767;
            
            // Store in buffer
            this.buffer[this.bufferIndex++] = Math.floor(pcmValue);
            
            // When buffer is full, send it to the main thread
            if (this.bufferIndex >= this.buffer.length) {
                this.port.postMessage({
                    audioData: this.buffer.slice(0)  // Send a copy of the buffer
                });
                
                // Reset buffer index
                this.bufferIndex = 0;
            }
        }
        
        // Keep processor alive
        return true;
    }
}

registerProcessor('audio-recorder-processor', AudioRecorderProcessor);

// --- Playback Processor ---

const BUFFER_SIZE = 8192; // Size of the ring buffer (adjust as needed)
const CROSSFADE_SAMPLES = 128; // Number of samples for crossfade (adjust as needed)

class PlaybackProcessor extends AudioWorkletProcessor {
    constructor(options) {
        super();
        this._buffer = new Float32Array(BUFFER_SIZE);
        this._writeIndex = 0;
        this._readIndex = 0;
        this._bufferFill = 0; // How many samples are currently in the buffer
        this._isPlaying = false; // Start paused until data arrives
        this._isStopping = false; // Flag to indicate stop request

        // Crossfading state
        this._crossfadeBuffer = new Float32Array(CROSSFADE_SAMPLES).fill(0);
        this._isCrossfadingIn = false;
        this._crossfadeIndex = 0;

        this.port.onmessage = (event) => {
            if (event.data.command === 'stop') {
                console.log('[PlaybackProcessor] Received stop command');
                this._isStopping = true; // Signal to stop after buffer drains or immediately if forced
                // Option: Clear buffer immediately? Depends on desired stop behavior.
                // this._readIndex = this._writeIndex;
                // this._bufferFill = 0;
                // this._isPlaying = false;
            } else if (event.data.command === 'clear') {
                 console.log('[PlaybackProcessor] Received clear command');
                 this._readIndex = this._writeIndex;
                 this._bufferFill = 0;
                 this._isPlaying = false;
                 this._isStopping = false;
                 this._crossfadeIndex = 0;
                 this._isCrossfadingIn = false;
                 this._crossfadeBuffer.fill(0);
            }
             else if (event.data.audioData) {
                this._handleAudioData(event.data.audioData);
                this._isPlaying = true; // Start playing once data arrives
                this._isStopping = false; // Resume playing if stopped
            }
        };
    }

    _handleAudioData(audioData) {
        const data = new Float32Array(audioData.buffer); // Assuming ArrayBuffer -> Float32Array

        if (this._bufferFill + data.length > BUFFER_SIZE) {
            console.warn('[PlaybackProcessor] Buffer overflow, discarding data.');
            // Optional: Overwrite oldest data instead of discarding new data
            // this._readIndex = (this._writeIndex + data.length) % BUFFER_SIZE;
            return;
        }

        // Prepare for crossfade-in if buffer was empty or starting fresh
        if (this._bufferFill === 0) {
             this._isCrossfadingIn = true;
             this._crossfadeIndex = 0;
             // Store the end of the previous (non-existent) chunk as silence
             this._crossfadeBuffer.fill(0);
        }


        // Copy data into the ring buffer
        for (let i = 0; i < data.length; i++) {
            this._buffer[this._writeIndex] = data[i];
            this._writeIndex = (this._writeIndex + 1) % BUFFER_SIZE;
        }
        this._bufferFill += data.length;

    }

    // Simple linear crossfade
    _applyCrossfade(sample, index, totalSamples, fadeIn, fadeOutBuffer) {
        const fadeOutGain = 1.0 - (index / totalSamples);
        const fadeInGain = index / totalSamples;
        return (fadeOutBuffer[index] * fadeOutGain) + (sample * fadeInGain);
    }


    process(inputs, outputs, parameters) {
        const output = outputs[0];
        const channel = output[0]; // Assuming mono output

        if (!this._isPlaying || channel === undefined) {
            // Output silence if not playing or no output channel
             if(channel) channel.fill(0);
            // If stopping and buffer is effectively empty, terminate
            if(this._isStopping && this._bufferFill < channel.length) {
                 console.log('[PlaybackProcessor] Stopping.');
                 this._isPlaying = false;
                 return false; // Request termination
            }
            return true; // Keep processor alive
        }


        let generatedSamples = 0;
        for (let i = 0; i < channel.length; i++) {
            if (this._bufferFill > 0) {
                let sample = this._buffer[this._readIndex];

                 // Apply crossfade-in if starting a new block after silence
                 if (this._isCrossfadingIn && this._crossfadeIndex < CROSSFADE_SAMPLES) {
                     sample = this._applyCrossfade(sample, this._crossfadeIndex, CROSSFADE_SAMPLES, true, this._crossfadeBuffer);
                     this._crossfadeIndex++;
                 } else {
                     this._isCrossfadingIn = false; // Done crossfading in
                 }


                channel[i] = sample;
                this._readIndex = (this._readIndex + 1) % BUFFER_SIZE;
                this._bufferFill--;
                generatedSamples++;


            } else {
                // Buffer underrun - fill with silence
                channel[i] = 0.0;
                 this._isPlaying = false; // Stop playing if buffer is empty
                 console.warn('[PlaybackProcessor] Buffer underrun.');
                 // Store the last samples for potential crossfade next time
                 if (generatedSamples > 0) {
                     const start = (this._readIndex - Math.min(generatedSamples, CROSSFADE_SAMPLES) + BUFFER_SIZE) % BUFFER_SIZE;
                      for(let j = 0; j < CROSSFADE_SAMPLES; j++) {
                          this._crossfadeBuffer[j] = this._buffer[(start + j) % BUFFER_SIZE] ?? 0.0;
                      }
                 } else {
                     this._crossfadeBuffer.fill(0); // No previous samples, fade from silence
                 }

                break; // Stop filling output buffer for this cycle
            }
        }
         // Fill remaining output buffer with silence if we stopped early due to underrun
         for (let i = generatedSamples; i < channel.length; i++) {
            channel[i] = 0.0;
         }


         // If stopping command received and buffer is now empty, request termination
         if (this._isStopping && this._bufferFill === 0) {
             console.log('[PlaybackProcessor] Stopping after draining buffer.');
             this._isPlaying = false;
             return false; // Request termination
         }


        return true; // Keep processor alive
    }
}

registerProcessor('playback-processor', PlaybackProcessor);

﻿// audio-processor.js
class AudioRecorderProcessor extends AudioWorkletProcessor {
    constructor() {
        super();
        // Create a buffer to store audio data (4096 samples)
        this.buffer = new Int16Array(4096);
        this.bufferIndex = 0;
        this.isActive = true;
        this.isStopping = false;
        this.stopCountdown = 0;
        
        // Listen for messages from the main thread
        this.port.onmessage = (event) => {
            if (event.data?.command === 'stop') {
                console.log("[AudioProcessor] Received stop command, initiating graceful shutdown");
                this.isStopping = true;
                // Process a few more frames before stopping (128 samples @ 48kHz = ~2.7ms)
                this.stopCountdown = 3;
            }
        };
    }

    process(inputs, outputs) {
        // Handle graceful shutdown
        if (this.isStopping) {
            if (this.stopCountdown > 0) {
                this.stopCountdown--;
                // Flush any remaining data in buffer
                if (this.bufferIndex > 0 && this.stopCountdown === 0) {
                    // Send the final partial buffer
                    this.port.postMessage({
                        audioData: this.buffer.slice(0, this.bufferIndex)
                    });
                    this.bufferIndex = 0;
                }
            } else {
                console.log("[AudioProcessor] Graceful shutdown complete");
                // Send confirmation that processor is stopping
                this.port.postMessage({ stopped: true });
                this.isActive = false;
                return false; // Terminate processor
            }
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

const BUFFER_SIZE = 1200000; // ~50s at 24kHz – prevents overflow on very long responses
const CROSSFADE_SAMPLES = 128; // Number of samples for crossfade (adjust as needed)
const MIN_START_BUFFER = 4800; // ~200 ms @ 24 kHz – buffer before starting playback

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
        
        // Audio enhancement state
        this._prevSample = 0; // For interpolation
        this._eqHistory = new Float32Array(4).fill(0); // For simple EQ
        this._noiseGate = 0.002; // Simple noise gate threshold
        this._enhancementEnabled = false; // Disabled by default for stability

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
                 this._buffer.fill(0); // Clear the entire buffer
            } else if (event.data.command === 'setEnhancement') {
                this._enhancementEnabled = !!event.data.enabled;
                console.log('[PlaybackProcessor] Audio enhancement:', this._enhancementEnabled ? 'enabled' : 'disabled');
            }
             else if (event.data.audioData) {
                this._handleAudioData(event.data.audioData);
                // Start playing only when we have a bit of buffered audio to avoid underruns
                if (!this._isPlaying && this._bufferFill >= MIN_START_BUFFER) {
                    this._isPlaying = true;
                }
                this._isStopping = false; // Resume playing if stopped
            }
        };
    }

    _handleAudioData(audioData) {
        const data = audioData instanceof ArrayBuffer ? new Float32Array(audioData) : new Float32Array(audioData.buffer);

        if (this._bufferFill + data.length > BUFFER_SIZE) {
            console.warn('[PlaybackProcessor] Buffer overflow, dropping new data.');
            return; // skip this chunk to preserve already queued audio
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
    
    // Audio enhancement: Simple interpolation for smoother playback
    _interpolate(currentSample) {
        // Linear interpolation between samples
        const interpolated = (this._prevSample + currentSample) * 0.5;
        this._prevSample = currentSample;
        return interpolated;
    }
    
    // Audio enhancement: Simple voice EQ (boosts mid frequencies for clarity)
    _applyVoiceEQ(sample) {
        // Simplified one-pole filter for voice enhancement
        // This is more stable and compatible
        const alpha = 0.15; // Filter coefficient
        const boost = 1.1; // Slight boost factor
        
        // Apply simple high-pass to remove DC and boost mids
        const filtered = sample - this._eqHistory[0];
        this._eqHistory[0] = this._eqHistory[0] + alpha * filtered;
        
        return sample + (filtered * boost * 0.3);
    }
    
    // Audio enhancement: Simple de-emphasis filter to reduce harshness
    _applyDeEmphasis(sample) {
        // Gentle high-frequency roll-off
        const alpha = 0.85;
        return sample * (1 - alpha) + this._prevSample * alpha;
    }
    
    // Audio enhancement: Process sample through enhancement chain
    _enhanceAudio(sample) {
        // Apply noise gate
        if (Math.abs(sample) < this._noiseGate) {
            sample = 0;
        }
        
        // Apply voice EQ for clarity
        sample = this._applyVoiceEQ(sample);
        
        // Apply de-emphasis to reduce harshness
        sample = this._applyDeEmphasis(sample);
        
        // Simple clipping protection
        if (sample > 1.0) {
            sample = 1.0;
        } else if (sample < -1.0) {
            sample = -1.0;
        }
        
        return sample;
    }


    process(inputs, outputs, parameters) {
        const output = outputs[0];
        const channel = output[0]; // mono

        // If we're stopped or have no channel, output silence
        if (!channel || this._isStopping) {
            if (channel) channel.fill(0);
            return true;
        }

        // Decide whether we should start or pause playback based on buffer level
        if (!this._isPlaying) {
            if (this._bufferFill >= MIN_START_BUFFER) {
                this._isPlaying = true; // Enough buffered, start playback
            } else {
                // Not enough buffered yet – output silence and wait
                if (channel) channel.fill(0);
                return true;
            }
        }

        if (channel === undefined) {
            return true;
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


                // Apply audio enhancement if enabled
                if (this._enhancementEnabled) {
                    sample = this._enhanceAudio(sample);
                }
                
                channel[i] = sample;
                this._readIndex = (this._readIndex + 1) % BUFFER_SIZE;
                this._bufferFill--;
                generatedSamples++;


            } else {
                // Buffer underrun - fill with silence
                channel[i] = 0.0;
                 // Not enough data – pause playback until buffer refills
                 if (this._bufferFill < MIN_START_BUFFER) {
                     this._isPlaying = false;
                 }
                  // Store the last samples for potential crossfade next time
                 if (generatedSamples > 0) {
                     const start = (this._readIndex - Math.min(generatedSamples, CROSSFADE_SAMPLES) + BUFFER_SIZE) % BUFFER_SIZE;
                      for(let j = 0; j < CROSSFADE_SAMPLES; j++) {
                          this._crossfadeBuffer[j] = this._buffer[(start + j) % BUFFER_SIZE] ?? 0.0;
                      }
                 } else {
                     this._crossfadeBuffer.fill(0); // No previous samples, fade from silence
                 }

                // continue filling remaining samples with silence
                continue;
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

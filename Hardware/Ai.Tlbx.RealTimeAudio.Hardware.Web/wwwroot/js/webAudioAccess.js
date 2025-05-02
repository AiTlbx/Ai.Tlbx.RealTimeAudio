// webAudioAccess.js
let mediaStream = null;
let audioWorkletNode = null;
let playbackWorkletNode = null;
let dotNetReference = null;
let isRecording = false;
let audioContext = null;
let audioInitialized = false;
let recordingInterval = null;
let playbackSampleRate = 24000;
let playbackNodeConnected = false;

// Utility function to load the audio worklet modules
async function loadAudioWorkletModules() {
    if (!audioContext) {
        console.error("Cannot load audio worklet: audioContext is null");
        return false;
    }
    
    try {
        // Ensure paths are correct relative to where the script is loaded (likely index.html)
        // Adjust path if necessary, e.g., '/js/audio-processor.js' if served from root
        await audioContext.audioWorklet.addModule('./js/audio-processor.js');
        console.log("AudioWorklet modules loaded successfully (or already loaded)");
        return true;
    } catch (err) {
         // Check if error is about module already being loaded - this is common and OK
         if (err.message && (err.message.includes('already been added') || err.message.includes('has been already registered'))) {
             console.warn("AudioWorklet module loading warning (likely already loaded):", err.message);
             return true; // Consider it loaded if it was already there
        }
        console.error("Failed to load AudioWorklet module:", err);
        return false;
    }
}

// This function needs an actual user interaction before it's called
async function initAudioWithUserInteraction() {
    try {
        console.log("Initializing audio with user interaction");
        
        // Create AudioContext with the correct sample rate for OpenAI/Playback
        // Check if context already exists and matches rate, reuse if possible
        if (!audioContext || audioContext.sampleRate !== playbackSampleRate) {
             if (audioContext) {
                 await audioContext.close(); // Close existing context if rate mismatches
             }
             audioContext = new (window.AudioContext || window.webkitAudioContext)({ sampleRate: playbackSampleRate });
             console.log(`AudioContext created/recreated with sample rate: ${audioContext.sampleRate}`);
        } else {
             console.log(`Reusing existing AudioContext with sample rate: ${audioContext.sampleRate}`);
        }


        console.log("AudioContext initial state:", audioContext.state);
        
        // Force resume the AudioContext - this requires user interaction in many browsers
        if (audioContext.state === 'suspended') {
            await audioContext.resume();
            console.log("AudioContext resumed, new state:", audioContext.state);
        }
        
        // Check if browser supports getUserMedia
        if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
            throw new Error("This browser doesn't support accessing the microphone. Please try Chrome, Firefox, or Edge.");
        }

        // Explicitly request microphone permissions first with a simpler configuration
        console.log("Requesting initial microphone permission...");
        try {
            // Try getting a dummy stream to ensure permissions are granted *before* enumerating
            const tempStream = await navigator.mediaDevices.getUserMedia({ audio: true });
            tempStream.getTracks().forEach(track => track.stop()); // Stop the dummy stream immediately
            console.log("Microphone permission appears granted.");
        } catch (permErr) {
            if (permErr.name === 'NotAllowedError' || permErr.name === 'PermissionDeniedError') {
                throw new Error("Microphone permission denied. Please allow microphone access in your browser settings and reload the page.");
            } else if (permErr.name === 'NotFoundError') {
                 throw new Error("No microphone detected. Please connect a microphone and reload the page.");
            }
            else {
                 console.warn("Error requesting initial mic permission stream:", permErr);
                 // Attempt to continue, maybe permissions exist from previous session
            }
        }


        // Load the AudioWorklet modules once
        if (!await loadAudioWorkletModules()) {
            throw new Error("Failed to load AudioWorklet modules");
        }

        // --- Setup Playback Worklet Node ---
        // Close existing node if present
        if (playbackWorkletNode) {
            console.log("Disconnecting existing playback worklet node");
            playbackWorkletNode.disconnect();
            playbackWorkletNode = null;
        }
         try {
             console.log("Creating PlaybackProcessor node");
             playbackWorkletNode = new AudioWorkletNode(audioContext, 'playback-processor');
             playbackWorkletNode.onprocessorerror = (event) => {
                 console.error('PlaybackProcessor error:', event);
                 dotNetReference?.invokeMethodAsync('OnAudioError', 'Playback processor error occurred.');
             };
             // Connect playback node to destination
             playbackWorkletNode.connect(audioContext.destination);
             playbackNodeConnected = true;
             console.log("PlaybackProcessor node created and connected.");
         } catch (nodeError) {
              console.error("Failed to create or connect PlaybackProcessor node:", nodeError);
              throw new Error(`Failed to initialize playback processor: ${nodeError.message}`);
         }
         // ----------------------------------


        // Test the microphone by creating a dummy recording setup (optional but good sanity check)
        try {
             console.log("Performing microphone initialization test...");
        const stream = await navigator.mediaDevices.getUserMedia({
            audio: {
                channelCount: 1,
                     sampleRate: playbackSampleRate, // Use consistent rate
                echoCancellation: true,
                noiseSuppression: true,
                autoGainControl: true
            },
            video: false
        });
        
        if (stream.getAudioTracks().length === 0) {
                 throw new Error("No audio tracks received from microphone during test.");
             }

             const source = audioContext.createMediaStreamSource(stream);
             // Use the actual recorder processor for the test
             const testRecordNode = new AudioWorkletNode(audioContext, 'audio-recorder-processor');
             source.connect(testRecordNode);
             testRecordNode.port.onmessage = (event) => { /* Process test data if needed, or ignore */ };

             await new Promise(resolve => setTimeout(resolve, 200)); // Short delay for test

             testRecordNode.disconnect();
             source.disconnect(); // Disconnect source as well
             stream.getTracks().forEach(track => track.stop());
             console.log("Microphone initialization test completed.");
        } catch(micTestError) {
             console.error("Microphone initialization test failed:", micTestError);
             // Decide if this is fatal or just a warning
             // throw new Error(`Microphone test failed: ${micTestError.message}`);
             dotNetReference?.invokeMethodAsync('OnAudioError', `Microphone test failed: ${micTestError.message}. Recording might not work.`);
        }

        
        audioInitialized = true;
        console.log("Audio system fully initialized (including playback processor)");
        return true;
    } catch (error) {
        console.error('Audio initialization error:', error);
        
        // Provide more specific error messages based on the error
        if (error.name === 'NotAllowedError' || error.name === 'PermissionDeniedError') {
            dotNetReference?.invokeMethodAsync('OnAudioError', 'Microphone permission denied. Please allow microphone access in your browser settings and reload the page.');
        } else if (error.name === 'NotFoundError') {
            dotNetReference?.invokeMethodAsync('OnAudioError', 'No microphone detected. Please connect a microphone and reload the page.');
        } else if (error.name === 'NotReadableError') {
            dotNetReference?.invokeMethodAsync('OnAudioError', 'Microphone is busy or not readable. Please check if another application is using your microphone.');
        } else {
            dotNetReference?.invokeMethodAsync('OnAudioError', `Audio initialization failed: ${error.message}`);
        }
        
        audioInitialized = false;
        // Cleanup partially initialized resources
         if (playbackWorkletNode) {
             playbackWorkletNode.disconnect();
             playbackWorkletNode = null;
         }
        if (audioContext && audioContext.state !== 'closed') {
            await audioContext.close();
            audioContext = null;
        }
        return false;
    }
}

// Legacy function for compatibility
async function initAudioPermissions() {
    console.warn("initAudioPermissions is deprecated, use initAudioWithUserInteraction");
    return await initAudioWithUserInteraction();
}

// Make sure AudioContext is resumed - must be called after user interaction
async function ensureAudioContextResumed() {
    if (!audioContext) {
        console.warn("ensureAudioContextResumed called but audioContext is null. Attempting reinitialization.");
        // Try a lightweight init if context is missing
         try {
             audioContext = new (window.AudioContext || window.webkitAudioContext)({ sampleRate: playbackSampleRate });
             await loadAudioWorkletModules(); // Need modules loaded too
             // Setup playback node again if missing
              if (!playbackWorkletNode && audioContext.state !== 'closed') {
                   playbackWorkletNode = new AudioWorkletNode(audioContext, 'playback-processor');
                   playbackWorkletNode.connect(audioContext.destination);
              }
              console.log("AudioContext created/reinitialized in ensure function, state:", audioContext.state);
         } catch (initErr) {
              console.error("Failed to reinitialize AudioContext in ensure function:", initErr);
              return false;
         }
    }
    
    if (audioContext.state === 'suspended') {
        try {
            console.log("Attempting to resume AudioContext in ensure function");
            await audioContext.resume();
            console.log("AudioContext resumed, new state:", audioContext.state);
        } catch (error) {
            console.error("Failed to resume AudioContext:", error);
            dotNetReference?.invokeMethodAsync('OnAudioError', 'Failed to resume audio context. Please interact with the page (click/tap).');
            return false;
        }
    }
     // Ensure playback node is connected
     if (playbackWorkletNode && playbackWorkletNode.context.state === 'running' && !playbackNodeConnected) {
          try {
              playbackWorkletNode.connect(audioContext.destination);
              playbackNodeConnected = true;
              console.log("Reconnected playback node in ensure function");
          } catch(e){ console.error("Failed to reconnect playback node:", e); }
      }

    return audioContext.state === 'running';
}

// New function to get available microphones
async function getAvailableMicrophones() {
    try {
        console.log("Getting available microphone devices");
        
        // Check if permission is already granted by the browser
        let permissionStatus = null;
        try {
            permissionStatus = await navigator.permissions.query({ name: 'microphone' });
            console.log("Microphone permission status:", permissionStatus.state);
            if (permissionStatus.state === 'denied') {
                 console.warn("Microphone permission denied by browser setting.");
                 // Return empty list or throw specific error? Return empty for now.
                 return [];
            }
        } catch (err) {
            console.log("Permission API not supported or failed, will try direct method", err);
        }
        
        // Get initial device list
        let devices = await navigator.mediaDevices.enumerateDevices();
        
        // Check if we already have labeled devices (this happens when permission is already granted)
        let hasLabels = devices.some(device => device.kind === 'audioinput' && device.label);
        
        // If we don't have labels AND permission wasn't explicitly granted or prompted
        if (!hasLabels && permissionStatus?.state !== 'granted') {
            console.log("No device labels available, requesting microphone access to get labels");
            try {
                // Request microphone access explicitly to trigger prompt if needed and get labels
                const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
                
                // Now we should have permission, get the devices again with labels
                devices = await navigator.mediaDevices.enumerateDevices();
                
                // Stop the stream immediately as we only needed it for permissions/labels
                stream.getTracks().forEach(track => track.stop());
            } catch (err) {
                // Handle potential errors during permission request (e.g., user denies)
                console.error("Error requesting microphone access for device labels:", err);
                 if (err.name === 'NotAllowedError' || err.name === 'PermissionDeniedError') {
                    dotNetReference?.invokeMethodAsync('OnAudioError', 'Microphone permission denied. Cannot list microphones.');
                 } else {
                     dotNetReference?.invokeMethodAsync('OnAudioError', `Error getting microphone list: ${err.message}`);
                 }
                return []; // Return empty list on error
            }
        }
        
        // Filter for audio input devices and map to the expected format
        const microphones = devices
            .filter(device => device.kind === 'audioinput')
            .map(device => ({
                    id: device.deviceId,
                name: device.label || `Microphone ${device.deviceId.substring(0, 8)}` // Provide a fallback name
            }));
        
        console.log("Available microphones:", microphones);
        return microphones;
    } catch (error) {
        console.error('Error getting available microphones:', error);
        dotNetReference?.invokeMethodAsync('OnAudioError', `Failed to enumerate microphones: ${error.message}`);
        return []; // Return empty list on error
    }
}

// Helper function to extract base device name
function getBaseDeviceName(fullName) {
    return fullName; // Simplification for now
}

// --- Recording ---
async function startRecording(dotNetObj, intervalMs = 500, deviceId = null) {
    console.log(`Attempting to start recording with interval ${intervalMs}ms, deviceId: ${deviceId}`);
    if (!(await ensureAudioContextResumed())) { // Ensure context is running first!
         console.error("Cannot start recording: AudioContext not running.");
         dotNetReference?.invokeMethodAsync('OnAudioError', 'Cannot start recording: AudioContext is not active. Please interact with the page.');
                return false;
            }
    if (!audioInitialized) {
        console.warn("Audio system not yet initialized – attempting initialization now.");
        const ok = await initAudioWithUserInteraction();
        if (!ok) {
            console.error("Cannot start recording: initAudioWithUserInteraction failed.");
            dotNetReference?.invokeMethodAsync('OnAudioError', 'Audio system not initialized. Please initialize first.');
            return false;
        }
    }
    if (isRecording) {
        console.warn("Recording already in progress.");
        return true; // Or false? Indicate it's already running.
    }

    dotNetReference = dotNetObj; // Store reference

    try {
        console.log(`Attempting to get media stream for device: ${deviceId || 'default'}`);
            const constraints = {
                audio: {
                    channelCount: 1,
                sampleRate: playbackSampleRate, // Use consistent rate
                    echoCancellation: true,
                    noiseSuppression: true,
                autoGainControl: true,
                ...(deviceId && { deviceId: { exact: deviceId } }) // Apply specific device ID if provided
                },
                video: false
            };
        mediaStream = await navigator.mediaDevices.getUserMedia(constraints);
        console.log("Media stream obtained successfully.");

        // Recreate recorder worklet node if needed (e.g., after context recreation)
        if (!audioWorkletNode || audioWorkletNode.context !== audioContext) {
             if (audioWorkletNode) {
                 audioWorkletNode.disconnect(); // Disconnect old one if context changed
             }
             console.log("Creating AudioRecorderProcessor node");
             audioWorkletNode = new AudioWorkletNode(audioContext, 'audio-recorder-processor');
             audioWorkletNode.onprocessorerror = (e) => console.error("Recorder processor error", e);
            } else {
            // Ensure it's active if reusing
             audioWorkletNode.port.postMessage({ command: 'start' }); // Add a 'start' command if needed by processor
        }


        // Setup message handling from the recorder worklet
        audioWorkletNode.port.onmessage = (event) => {
            if (event.data.audioData) {
                // Convert Int16Array buffer back to Base64 string
                const pcm16Data = event.data.audioData; // This is Int16Array from processor
                const buffer = pcm16Data.buffer; // Get ArrayBuffer
                const bytes = new Uint8Array(buffer);
                let binary = '';
                for (let i = 0; i < bytes.byteLength; i++) {
                    binary += String.fromCharCode(bytes[i]);
                }
                const base64Audio = btoa(binary);
                // console.log(`[js] Sending audio chunk: ${base64Audio.substring(0, 30)}...`);
                dotNetReference?.invokeMethodAsync('OnAudioDataAvailable', base64Audio);
            }
        };

        const source = audioContext.createMediaStreamSource(mediaStream);
            source.connect(audioWorkletNode);
        // Do NOT connect recorder worklet to destination
        // audioWorkletNode.connect(audioContext.destination); // NO! This would cause feedback
        
        isRecording = true;
        console.log("Recording started.");
        dotNetReference?.invokeMethodAsync('OnRecordingStateChanged', true); // Notify C#
        return true;

    } catch (error) {
        console.error('Error starting recording:', error);
        isRecording = false;
         dotNetReference?.invokeMethodAsync('OnRecordingStateChanged', false); // Notify C#
         // Provide specific error messages
         if (error.name === 'NotAllowedError' || error.name === 'PermissionDeniedError') {
             dotNetReference?.invokeMethodAsync('OnAudioError', 'Microphone permission denied. Cannot start recording.');
         } else if (error.name === 'NotFoundError' || error.name === 'OverconstrainedError') {
             dotNetReference?.invokeMethodAsync('OnAudioError', `Selected microphone (ID: ${deviceId}) not found or constraints unmet. Please check selection or hardware.`);
         } else if (error.name === 'NotReadableError') {
              dotNetReference?.invokeMethodAsync('OnAudioError', 'Microphone is busy or unreadable. Check if another app is using it.');
         } else {
             dotNetReference?.invokeMethodAsync('OnAudioError', `Failed to start recording: ${error.message}`);
         }

        // Clean up partial setup
        if (mediaStream) {
            mediaStream.getTracks().forEach(track => track.stop());
            mediaStream = null;
        }
        // Don't null out audioWorkletNode here, it might be needed later
        return false;
    }
}

// --- Stop Recording ---
async function stopRecording() {
    console.log("Attempting to stop recording.");
        if (!isRecording) {
        console.warn("Recording not in progress.");
            return;
        }
        
    isRecording = false; // Set flag immediately

     // Signal the worklet processor to stop processing new audio
     if (audioWorkletNode) {
         console.log("Sending stop command to recorder worklet.");
         try {
              audioWorkletNode.port.postMessage({ command: 'stop' });
              // Optional: Disconnect immediately? Or let it process remaining buffer?
              // audioWorkletNode.disconnect();
         } catch(e) { console.error("Error sending stop message to recorder worklet:", e); }

     }

    // Stop the media stream tracks
    if (mediaStream) {
        console.log("Stopping media stream tracks.");
        mediaStream.getTracks().forEach(track => track.stop());
        mediaStream = null;
    } else {
        console.warn("stopRecording called but mediaStream was already null.");
    }


    // Clear interval if it was used (should not be with worklet)
        if (recordingInterval) {
            clearInterval(recordingInterval);
            recordingInterval = null;
        }
        
     // audioChunks = []; // Clear any old chunks if array still exists

    console.log("Recording stopped.");
    dotNetReference?.invokeMethodAsync('OnRecordingStateChanged', false); // Notify C#
}

// --- Audio Playback ---

// Function to convert Base64 PCM16 string to Float32Array [-1.0, 1.0]
function pcm16Base64ToFloat32(base64Audio) {
    try {
        const binaryString = atob(base64Audio);
        const len = binaryString.length;
        const bytes = new Uint8Array(len);
        for (let i = 0; i < len; i++) {
            bytes[i] = binaryString.charCodeAt(i);
        }

        // Assuming the byte stream is little-endian PCM16
        const pcm16 = new Int16Array(bytes.buffer);
        const float32 = new Float32Array(pcm16.length);

        for (let i = 0; i < pcm16.length; i++) {
            float32[i] = pcm16[i] / 32768.0; // Normalize to [-1.0, 1.0)
        }
        return float32;
    } catch (error) {
        console.error("Error decoding/converting Base64 PCM16 audio:", error);
        return null;
    }
}

// Main function to play audio using the worklet
async function playAudio(base64Audio, sampleRate = 24000) {
     // console.log(`[js] Received playAudio request, sampleRate: ${sampleRate}, data: ${base64Audio.substring(0,30)}...`);

     // 1. Ensure AudioContext is running and playback node exists
     if (!(await ensureAudioContextResumed())) {
         console.error("Cannot play audio: AudioContext not running.");
          dotNetReference?.invokeMethodAsync('OnAudioError', 'Cannot play audio: AudioContext is not active. Please interact with the page.');
         return false; // Indicate failure
     }
      if (!playbackWorkletNode) {
         console.error("Cannot play audio: Playback worklet node not initialized.");
         dotNetReference?.invokeMethodAsync('OnAudioError', 'Playback system not initialized.');
         // Attempt re-initialization? Risky without user interaction context.
         // await initAudioWithUserInteraction(); // Be careful calling this here
          return false;
     }

     // 2. Validate Sample Rate (Currently handled by assuming 24kHz input matches context)
     if (sampleRate !== audioContext.sampleRate) {
         console.warn(`Audio sample rate mismatch: Input is ${sampleRate}Hz, Context is ${audioContext.sampleRate}Hz. Audio quality may be affected. Implement resampling if needed.`);
         // TODO: Implement resampling here if needed (e.g., using OfflineAudioContext or a library)
         // For now, we proceed, but quality might be bad if rates differ significantly.
     }

     // 3. Decode Base64 and Convert PCM16 to Float32
     const float32Audio = pcm16Base64ToFloat32(base64Audio);
     if (!float32Audio) {
         console.error("Failed to decode audio data.");
         dotNetReference?.invokeMethodAsync('OnAudioError', 'Failed to decode received audio data.');
         return false;
     }
      // console.log(`[js] Decoded ${float32Audio.length} samples.`);

     // 4. Send data to the PlaybackProcessor asynchronously to prevent blocking main thread
     try {
         playbackWorkletNode.port.postMessage({ audioData: float32Audio.buffer }, [float32Audio.buffer]);
         return true;
     } catch (error) {
         console.error("Error sending audio data to PlaybackProcessor:", error);
         dotNetReference?.invokeMethodAsync('OnAudioError', `Error sending audio data for playback: ${error.message}`);
         return false; // Indicate failure
     }
}

// Function to stop audio playback and clear buffers
async function stopAudioPlayback() {
    console.log("Attempting to stop audio playback.");

    if (!audioContext || !playbackWorkletNode) {
        console.warn("Cannot stop playback: Audio context or playback node not initialized.");
        return;
    }

    // Send 'clear' message to the worklet to immediately stop and clear its buffer
    try {
        console.log("Sending 'clear' command to PlaybackProcessor.");
        playbackWorkletNode.port.postMessage({ command: 'clear' });
        playbackNodeConnected = true; // still connected but cleared buffer
    } catch (error) {
        console.error("Error sending 'clear' command to PlaybackProcessor:", error);
    }

    // Note: The PlaybackProcessor should handle stopping itself when it receives 'clear'
    // or when its buffer runs dry after a 'stop' command (if we used 'stop' instead of 'clear').
    // We don't need to manage individual AudioBufferSourceNodes anymore.

    console.log("Playback stop requested.");
}

// --- Microphone Testing --- (Optional - Adapt if needed)
// This likely needs adjustment if it relied on the old playback mechanism.
// For now, assume it's not the primary focus. If a mic test UI exists,
// it might need to pipe recorder output directly back to the playback worklet.

// Example: A simple mic test function might look like this:
let isMicTesting = false;
let micTestSource = null;
let micTestRecorderNode = null;
let micTestPlaybackNode = null; // Could reuse the main playback node

async function startMicTest() {
    if (!(await ensureAudioContextResumed())) return;
    if (!audioInitialized || !playbackWorkletNode) {
         console.error("Cannot start mic test: Audio system not ready.");
         return;
    }
     if (isMicTesting) return;

     console.log("Starting mic test loopback.");
     isMicTesting = true;

     try {
        const stream = await navigator.mediaDevices.getUserMedia({ audio: { /* constraints */ } });
        micTestSource = audioContext.createMediaStreamSource(stream);

        // Recorder Node (can reuse main one if not recording, or create temp)
        micTestRecorderNode = new AudioWorkletNode(audioContext, 'audio-recorder-processor');
        micTestRecorderNode.port.onmessage = (event) => {
            if (event.data.audioData) {
                // Convert Int16 to Float32
                const pcm16 = event.data.audioData;
                const float32 = new Float32Array(pcm16.length);
                for(let i=0; i<pcm16.length; i++) float32[i] = pcm16[i] / 32768.0;
                // Send to playback worklet
                 if (playbackWorkletNode) {
                     playbackWorkletNode.port.postMessage({ audioData: float32.buffer }, [float32.buffer]);
                 }
            }
        };

        micTestSource.connect(micTestRecorderNode);
        // DO NOT connect micTestRecorderNode directly to destination

     } catch(err) {
         console.error("Mic test start failed:", err);
         stopMicTest(); // Clean up
     }
}

function stopMicTest() {
     if (!isMicTesting) return;
     console.log("Stopping mic test loopback.");
     isMicTesting = false;

     if (micTestRecorderNode) {
         micTestRecorderNode.port.postMessage({ command: 'stop' }); // Tell processor to stop
         micTestRecorderNode.disconnect();
         micTestRecorderNode = null;
     }
     if (micTestSource) {
         micTestSource.mediaStream.getTracks().forEach(track => track.stop());
         micTestSource.disconnect();
         micTestSource = null;
     }
      // Tell playback node to clear any test audio
      if (playbackWorkletNode) {
         playbackWorkletNode.port.postMessage({ command: 'clear' });
      }
}

// Function called by Blazor to set the .NET object reference
function setDotNetReference(dotNetRef) {
    dotNetReference = dotNetRef;
    console.log("DotNet reference set.");
}

// Clean up function (optional but good practice)
function cleanupAudio() {
     console.log("Cleaning up audio resources.");
     stopRecording();
     stopAudioPlayback();
     stopMicTest(); // Ensure mic test is stopped

     if (playbackWorkletNode) {
         playbackWorkletNode.disconnect();
         playbackWorkletNode = null;
     }
      if (audioWorkletNode) { // Recorder node
         audioWorkletNode.disconnect();
         audioWorkletNode = null;
     }

     if (audioContext && audioContext.state !== 'closed') {
         audioContext.close().then(() => console.log("AudioContext closed."));
         audioContext = null;
     }
     audioInitialized = false;
     dotNetReference = null;
     playbackNodeConnected = false;
}

// Attach functions to window for backwards compatibility
window.audioInterop = {
    initAudioWithUserInteraction,
    getAvailableMicrophones,
    startRecording,
    stopRecording,
    playAudio,
    stopAudioPlayback,
    setDotNetReference,
    startMicTest,
    stopMicTest,
    cleanupAudio
};

// Export individually for ES module consumers
export {
    initAudioWithUserInteraction,
    getAvailableMicrophones,
    startRecording,
    stopRecording,
    playAudio,
    stopAudioPlayback,
    setDotNetReference,
    startMicTest,
    stopMicTest,
    cleanupAudio
};

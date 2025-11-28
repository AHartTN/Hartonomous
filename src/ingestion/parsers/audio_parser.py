"""
Audio parser - handles audio files, spectrograms, and audio features.
Supports WAV, MP3, FLAC, and other formats.
"""

import numpy as np
from typing import Dict, Any, Iterator, Optional, Tuple
from pathlib import Path

from ...core.atomization import Atomizer, ModalityType
from ...core.landmark import LandmarkProjector


class AudioParser:
    """Parse and atomize audio files."""
    
    def __init__(self):
        self.atomizer = Atomizer()
        self.landmark_projector = LandmarkProjector()
        self.supported_formats = ['.wav', '.mp3', '.flac', '.ogg', '.m4a']
    
    def _load_audio(self, audio_path: Path) -> Tuple[np.ndarray, int]:
        """Load audio file and return waveform + sample rate."""
        try:
            import librosa
            audio, sr = librosa.load(str(audio_path), sr=None, mono=False)
            return audio.astype(np.float64), sr
        except ImportError:
            raise ImportError("librosa required for audio parsing: pip install librosa")
    
    def _extract_spectral_features(self, audio: np.ndarray, sr: int) -> Dict[str, np.ndarray]:
        """Extract spectral features from audio."""
        import librosa
        
        features = {}
        
        # Mel spectrogram
        mel_spec = librosa.feature.melspectrogram(y=audio, sr=sr, n_mels=128)
        features['mel_spectrogram'] = librosa.power_to_db(mel_spec, ref=np.max).astype(np.float64)
        
        # MFCC
        mfcc = librosa.feature.mfcc(y=audio, sr=sr, n_mfcc=40)
        features['mfcc'] = mfcc.astype(np.float64)
        
        # Chroma
        chroma = librosa.feature.chroma_stft(y=audio, sr=sr)
        features['chroma'] = chroma.astype(np.float64)
        
        # Spectral contrast
        contrast = librosa.feature.spectral_contrast(y=audio, sr=sr)
        features['spectral_contrast'] = contrast.astype(np.float64)
        
        # Zero crossing rate
        zcr = librosa.feature.zero_crossing_rate(audio)
        features['zero_crossing_rate'] = zcr.astype(np.float64)
        
        return features
    
    def parse(
        self,
        audio_path: Path,
        extract_features: bool = True,
        chunk_duration: float = 1.0
    ) -> Iterator[Dict[str, Any]]:
        """Parse audio file into atoms."""
        # Load audio
        audio, sr = self._load_audio(audio_path)
        
        # Ensure 1D for mono, or process channels separately
        if audio.ndim == 2:
            # Multiple channels - process each
            channels = audio
        else:
            channels = audio[np.newaxis, :]
        
        # Calculate chunk size in samples
        chunk_samples = int(chunk_duration * sr)
        
        for channel_idx, channel_audio in enumerate(channels):
            # Chunk audio
            num_chunks = int(np.ceil(len(channel_audio) / chunk_samples))
            
            for chunk_idx in range(num_chunks):
                start_idx = chunk_idx * chunk_samples
                end_idx = min((chunk_idx + 1) * chunk_samples, len(channel_audio))
                chunk = channel_audio[start_idx:end_idx]
                
                # Atomize raw audio samples
                audio_atoms = self.atomizer.atomize_audio(chunk, sr)
                
                # Extract features if requested
                if extract_features and len(chunk) > 512:
                    features = self._extract_spectral_features(chunk, sr)
                    
                    # Atomize each feature type
                    for feature_name, feature_data in features.items():
                        feature_atoms = self.atomizer.atomize_array(
                            feature_data,
                            ModalityType.AUDIO_FEATURE
                        )
                        landmarks = self.landmark_projector.extract_audio_landmarks(
                            feature_data,
                            feature_name
                        )
                        
                        for atom in feature_atoms:
                            for landmark in landmarks:
                                yield {
                                    'atom': atom,
                                    'landmark': landmark,
                                    'audio_path': str(audio_path),
                                    'channel': channel_idx,
                                    'chunk_index': chunk_idx,
                                    'feature_type': feature_name,
                                    'sample_rate': sr,
                                    'time_range': (start_idx / sr, end_idx / sr)
                                }
                
                # Also yield raw audio atoms with landmarks
                landmarks = self.landmark_projector.extract_audio_landmarks(chunk, 'raw')
                for atom in audio_atoms:
                    for landmark in landmarks:
                        yield {
                            'atom': atom,
                            'landmark': landmark,
                            'audio_path': str(audio_path),
                            'channel': channel_idx,
                            'chunk_index': chunk_idx,
                            'feature_type': 'raw_audio',
                            'sample_rate': sr,
                            'time_range': (start_idx / sr, end_idx / sr)
                        }

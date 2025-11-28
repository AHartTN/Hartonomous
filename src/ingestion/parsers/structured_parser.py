"""
Structured data parser - handles CSV, JSON, databases, and tabular data.
Extracts features and relationships.
"""

import numpy as np
import pandas as pd
from typing import Dict, Any, Iterator, List, Optional
from pathlib import Path

from ...core.atomization import Atomizer, ModalityType
from ...core.landmark import LandmarkProjector


class StructuredParser:
    """Parse and atomize structured data."""
    
    def __init__(self):
        self.atomizer = Atomizer()
        self.landmark_projector = LandmarkProjector()
        self.supported_formats = ['.csv', '.json', '.parquet', '.xlsx', '.sql']
    
    def _load_data(self, file_path: Path) -> pd.DataFrame:
        """Load structured data into DataFrame."""
        suffix = file_path.suffix.lower()
        
        if suffix == '.csv':
            return pd.read_csv(file_path)
        elif suffix == '.json':
            return pd.read_json(file_path)
        elif suffix == '.parquet':
            return pd.read_parquet(file_path)
        elif suffix == '.xlsx':
            return pd.read_excel(file_path)
        else:
            raise ValueError(f"Unsupported format: {suffix}")
    
    def _encode_column(self, series: pd.Series) -> np.ndarray:
        """Encode a column into numeric representation."""
        if pd.api.types.is_numeric_dtype(series):
            # Already numeric
            return series.fillna(0).astype(np.float64).values
        
        elif pd.api.types.is_categorical_dtype(series) or pd.api.types.is_object_dtype(series):
            # Categorical - use label encoding
            from sklearn.preprocessing import LabelEncoder
            encoder = LabelEncoder()
            encoded = encoder.fit_transform(series.fillna('__NULL__').astype(str))
            return encoded.astype(np.float64)
        
        elif pd.api.types.is_datetime64_any_dtype(series):
            # DateTime - convert to Unix timestamp
            return series.astype(np.int64).astype(np.float64) / 1e9
        
        else:
            # Fallback: convert to string and hash
            hashes = series.fillna('').astype(str).apply(hash).astype(np.float64)
            return hashes.values
    
    def parse(
        self,
        file_path: Path,
        chunk_size: int = 1000
    ) -> Iterator[Dict[str, Any]]:
        """Parse structured data file into atoms."""
        # Load data
        df = self._load_data(file_path)
        
        # Process in chunks
        num_chunks = int(np.ceil(len(df) / chunk_size))
        
        for chunk_idx in range(num_chunks):
            start_idx = chunk_idx * chunk_size
            end_idx = min((chunk_idx + 1) * chunk_size, len(df))
            chunk_df = df.iloc[start_idx:end_idx]
            
            # Process each column
            for col_name in chunk_df.columns:
                col_data = chunk_df[col_name]
                
                # Encode column
                encoded = self._encode_column(col_data)
                
                # Atomize
                atoms = self.atomizer.atomize_array(encoded, ModalityType.STRUCTURED_FIELD)
                landmarks = self.landmark_projector.extract_structured_landmarks(
                    encoded,
                    col_name,
                    str(col_data.dtype)
                )
                
                for atom in atoms:
                    for landmark in landmarks:
                        yield {
                            'atom': atom,
                            'landmark': landmark,
                            'file_path': str(file_path),
                            'column_name': col_name,
                            'column_dtype': str(col_data.dtype),
                            'chunk_index': chunk_idx,
                            'row_range': (start_idx, end_idx)
                        }
    
    def parse_dataframe(
        self,
        df: pd.DataFrame,
        source_id: str,
        chunk_size: int = 1000
    ) -> Iterator[Dict[str, Any]]:
        """Parse DataFrame directly."""
        num_chunks = int(np.ceil(len(df) / chunk_size))
        
        for chunk_idx in range(num_chunks):
            start_idx = chunk_idx * chunk_size
            end_idx = min((chunk_idx + 1) * chunk_size, len(df))
            chunk_df = df.iloc[start_idx:end_idx]
            
            for col_name in chunk_df.columns:
                col_data = chunk_df[col_name]
                encoded = self._encode_column(col_data)
                
                atoms = self.atomizer.atomize_array(encoded, ModalityType.STRUCTURED_FIELD)
                landmarks = self.landmark_projector.extract_structured_landmarks(
                    encoded,
                    col_name,
                    str(col_data.dtype)
                )
                
                for atom in atoms:
                    for landmark in landmarks:
                        yield {
                            'atom': atom,
                            'landmark': landmark,
                            'source_id': source_id,
                            'column_name': col_name,
                            'column_dtype': str(col_data.dtype),
                            'chunk_index': chunk_idx,
                            'row_range': (start_idx, end_idx)
                        }

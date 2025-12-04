"""
GPU-accelerated Byte Pair Encoding (BPE) learning algorithm.
Identifies frequent byte sequences for efficient content compression.
"""

def bpe_learning_gpu(byte_sequences, max_vocab_size, min_frequency):
    """
    Learn BPE vocabulary from byte sequences using GPU-accelerated pair counting.
    
    Args:
        byte_sequences: Array of byte sequences (each sequence is array of integers 0-255)
        max_vocab_size: Maximum vocabulary size to learn
        min_frequency: Minimum frequency threshold for pairs to be merged
    
    Returns:
        Array of tuples [(byte_pair, frequency), ...] representing learned merges
    """
    try:
        import cupy as cp
        import numpy as np
        from collections import Counter
        
        # Convert sequences to NumPy for initial processing
        sequences = [np.array(seq, dtype=np.int32) for seq in byte_sequences]
        
        learned_merges = []
        
        for iteration in range(max_vocab_size):
            # Count all adjacent pairs across all sequences
            pair_counts = Counter()
            
            for seq in sequences:
                if len(seq) < 2:
                    continue
                
                # Convert to CuPy for GPU processing
                seq_gpu = cp.array(seq)
                
                # Create pairs: (seq[i], seq[i+1])
                pairs_left = seq_gpu[:-1]
                pairs_right = seq_gpu[1:]
                
                # Move back to CPU for counting (CuPy doesn't have Counter)
                pairs_cpu = list(zip(cp.asnumpy(pairs_left), cp.asnumpy(pairs_right)))
                pair_counts.update(pairs_cpu)
            
            if not pair_counts:
                break
            
            # Find most frequent pair
            best_pair, best_freq = pair_counts.most_common(1)[0]
            
            if best_freq < min_frequency:
                break
            
            # Record this merge
            learned_merges.append((list(best_pair), int(best_freq)))
            
            # Merge the best pair in all sequences
            new_token = 256 + iteration  # New tokens start at 256
            new_sequences = []
            
            for seq in sequences:
                if len(seq) < 2:
                    new_sequences.append(seq)
                    continue
                
                seq_gpu = cp.array(seq)
                
                # Find positions where best_pair occurs
                matches = (seq_gpu[:-1] == best_pair[0]) & (seq_gpu[1:] == best_pair[1])
                
                if not cp.any(matches):
                    new_sequences.append(seq)
                    continue
                
                # Build new sequence with merged tokens
                new_seq = []
                i = 0
                seq_cpu = cp.asnumpy(seq_gpu)
                matches_cpu = cp.asnumpy(matches)
                
                while i < len(seq_cpu):
                    if i < len(matches_cpu) and matches_cpu[i]:
                        new_seq.append(new_token)
                        i += 2  # Skip both bytes of the pair
                    else:
                        new_seq.append(seq_cpu[i])
                        i += 1
                
                new_sequences.append(np.array(new_seq, dtype=np.int32))
            
            sequences = new_sequences
        
        return learned_merges
        
    except ImportError:
        # Fallback to CPU implementation
        import numpy as np
        from collections import Counter
        
        sequences = [np.array(seq, dtype=np.int32) for seq in byte_sequences]
        learned_merges = []
        
        for iteration in range(max_vocab_size):
            pair_counts = Counter()
            
            for seq in sequences:
                if len(seq) < 2:
                    continue
                pairs = list(zip(seq[:-1], seq[1:]))
                pair_counts.update(pairs)
            
            if not pair_counts:
                break
            
            best_pair, best_freq = pair_counts.most_common(1)[0]
            
            if best_freq < min_frequency:
                break
            
            learned_merges.append((list(best_pair), int(best_freq)))
            
            new_token = 256 + iteration
            new_sequences = []
            
            for seq in sequences:
                if len(seq) < 2:
                    new_sequences.append(seq)
                    continue
                
                new_seq = []
                i = 0
                while i < len(seq):
                    if i < len(seq) - 1 and seq[i] == best_pair[0] and seq[i+1] == best_pair[1]:
                        new_seq.append(new_token)
                        i += 2
                    else:
                        new_seq.append(seq[i])
                        i += 1
                
                new_sequences.append(np.array(new_seq, dtype=np.int32))
            
            sequences = new_sequences
        
        return learned_merges
    
    except Exception as e:
        import plpy
        plpy.warning(f"Error in bpe_learning_gpu: {str(e)}")
        return []

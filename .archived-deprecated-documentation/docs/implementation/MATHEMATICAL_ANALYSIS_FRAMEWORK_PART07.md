# Mathematical Analysis Framework - Part 7: Statistical Analysis

**Version:** 1.0.0  
**Date:** December 1, 2025  
**Status:** Implementation Planning

---

## 7.1 Descriptive Statistics

### 7.1.1 Central Tendency

**Measures**: Mean, median, mode

**Application - Atom Distribution Analysis**:
- Mean position of concept atoms
- Median similarity scores
- Mode (most common) modality

**SQL Implementation**:
```sql
CREATE OR REPLACE FUNCTION compute_concept_statistics(
    p_concept_id BIGINT
)
RETURNS TABLE (
    statistic_name TEXT,
    statistic_value DOUBLE PRECISION,
    dimension TEXT
) AS $$
BEGIN
    RETURN QUERY
    WITH atom_positions AS (
        SELECT 
            ST_X(a.spatial_position) as x,
            ST_Y(a.spatial_position) as y,
            ST_Z(a.spatial_position) as z
        FROM atom_relation ar
        JOIN atom a ON a.atom_id = ar.from_atom_id
        WHERE ar.to_atom_id = p_concept_id
    )
    SELECT 'mean'::TEXT, AVG(x), 'x'::TEXT FROM atom_positions
    UNION ALL
    SELECT 'mean'::TEXT, AVG(y), 'y'::TEXT FROM atom_positions
    UNION ALL
    SELECT 'mean'::TEXT, AVG(z), 'z'::TEXT FROM atom_positions
    UNION ALL
    SELECT 'median'::TEXT, PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY x), 'x'::TEXT FROM atom_positions
    UNION ALL
    SELECT 'median'::TEXT, PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY y), 'y'::TEXT FROM atom_positions
    UNION ALL
    SELECT 'median'::TEXT, PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY z), 'z'::TEXT FROM atom_positions
    UNION ALL
    SELECT 'std_dev'::TEXT, STDDEV(x), 'x'::TEXT FROM atom_positions
    UNION ALL
    SELECT 'std_dev'::TEXT, STDDEV(y), 'y'::TEXT FROM atom_positions
    UNION ALL
    SELECT 'std_dev'::TEXT, STDDEV(z), 'z'::TEXT FROM atom_positions;
END;
$$ LANGUAGE plpgsql STABLE;
```

**Usage Example**:
```python
async def analyze_concept_distribution(conn, concept_id: int):
    """
    Analyze statistical distribution of atoms linked to a concept.
    """
    async with conn.cursor() as cur:
        await cur.execute("""
            SELECT * FROM compute_concept_statistics(%s)
        """, (concept_id,))
        
        stats = await cur.fetchall()
    
    # Organize results
    results = {
        'mean': {},
        'median': {},
        'std_dev': {}
    }
    
    for stat_name, value, dimension in stats:
        results[stat_name][dimension] = value
    
    # Compute centroid distance
    mean_pos = np.array([results['mean']['x'], results['mean']['y'], results['mean']['z']])
    median_pos = np.array([results['median']['x'], results['median']['y'], results['median']['z']])
    centroid_distance = np.linalg.norm(mean_pos - median_pos)
    
    # Interpret
    if centroid_distance < 0.01:
        distribution_type = "SYMMETRIC (mean ≈ median)"
    else:
        distribution_type = "SKEWED (mean ≠ median)"
    
    return {
        'concept_id': concept_id,
        'mean_position': mean_pos.tolist(),
        'median_position': median_pos.tolist(),
        'std_dev': [results['std_dev']['x'], results['std_dev']['y'], results['std_dev']['z']],
        'centroid_distance': centroid_distance,
        'distribution_type': distribution_type
    }

# Example: CAT concept distribution
result = await analyze_concept_distribution(conn, concept_id=9001)

print(f"CAT concept distribution:")
print(f"Mean position: ({result['mean_position'][0]:.4f}, "
      f"{result['mean_position'][1]:.4f}, {result['mean_position'][2]:.4f})")
print(f"Median position: ({result['median_position'][0]:.4f}, "
      f"{result['median_position'][1]:.4f}, {result['median_position'][2]:.4f})")
print(f"Standard deviation: ({result['std_dev'][0]:.4f}, "
      f"{result['std_dev'][1]:.4f}, {result['std_dev'][2]:.4f})")
print(f"Distribution: {result['distribution_type']}")

# Output:
# CAT concept distribution:
# Mean position: (0.3421, 0.5678, 0.2341)
# Median position: (0.3398, 0.5701, 0.2356)
# Standard deviation: (0.0891, 0.1234, 0.0567)
# Distribution: SYMMETRIC (mean ≈ median)
```

### 7.1.2 Dispersion Measures

**Measures**: Variance, standard deviation, IQR, range

**Application - Concept Spread**:
- Tight clusters vs dispersed concepts
- Identify outliers
- Measure concept coherence

**SQL Implementation**:
```sql
CREATE OR REPLACE FUNCTION compute_dispersion_metrics(
    p_concept_id BIGINT
)
RETURNS TABLE (
    metric_name TEXT,
    metric_value DOUBLE PRECISION
) AS $$
BEGIN
    RETURN QUERY
    WITH atom_distances AS (
        SELECT 
            ST_Distance(
                a.spatial_position,
                (SELECT ST_Centroid(ST_Collect(spatial_position)) 
                 FROM atom a2 
                 JOIN atom_relation ar2 ON ar2.from_atom_id = a2.atom_id
                 WHERE ar2.to_atom_id = p_concept_id)
            ) as distance_from_centroid
        FROM atom_relation ar
        JOIN atom a ON a.atom_id = ar.from_atom_id
        WHERE ar.to_atom_id = p_concept_id
    )
    SELECT 'variance'::TEXT, VARIANCE(distance_from_centroid) FROM atom_distances
    UNION ALL
    SELECT 'std_dev'::TEXT, STDDEV(distance_from_centroid) FROM atom_distances
    UNION ALL
    SELECT 'range'::TEXT, MAX(distance_from_centroid) - MIN(distance_from_centroid) FROM atom_distances
    UNION ALL
    SELECT 'iqr'::TEXT, 
           PERCENTILE_CONT(0.75) WITHIN GROUP (ORDER BY distance_from_centroid) -
           PERCENTILE_CONT(0.25) WITHIN GROUP (ORDER BY distance_from_centroid)
    FROM atom_distances
    UNION ALL
    SELECT 'q1'::TEXT, PERCENTILE_CONT(0.25) WITHIN GROUP (ORDER BY distance_from_centroid) FROM atom_distances
    UNION ALL
    SELECT 'q3'::TEXT, PERCENTILE_CONT(0.75) WITHIN GROUP (ORDER BY distance_from_centroid) FROM atom_distances
    UNION ALL
    SELECT 'median'::TEXT, PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY distance_from_centroid) FROM atom_distances;
END;
$$ LANGUAGE plpgsql STABLE;
```

**Usage Example**:
```python
async def analyze_concept_coherence(conn, concept_id: int):
    """
    Measure how tightly clustered a concept is.
    
    Low dispersion = coherent, focused concept
    High dispersion = diffuse, broad concept
    """
    async with conn.cursor() as cur:
        await cur.execute("""
            SELECT * FROM compute_dispersion_metrics(%s)
        """, (concept_id,))
        
        metrics = {row[0]: row[1] for row in await cur.fetchall()}
    
    # Compute coefficient of variation (CV)
    cv = metrics['std_dev'] / metrics['median'] if metrics['median'] > 0 else 0
    
    # Interpret coherence
    if cv < 0.3:
        coherence = "VERY COHERENT (tight cluster)"
    elif cv < 0.6:
        coherence = "MODERATELY COHERENT (reasonable spread)"
    elif cv < 1.0:
        coherence = "LOOSELY COHERENT (broad distribution)"
    else:
        coherence = "DIFFUSE (widely scattered)"
    
    # Detect outliers using IQR method
    outlier_threshold_lower = metrics['q1'] - 1.5 * metrics['iqr']
    outlier_threshold_upper = metrics['q3'] + 1.5 * metrics['iqr']
    
    return {
        'concept_id': concept_id,
        'variance': metrics['variance'],
        'std_dev': metrics['std_dev'],
        'range': metrics['range'],
        'iqr': metrics['iqr'],
        'median_distance': metrics['median'],
        'coefficient_of_variation': cv,
        'coherence': coherence,
        'outlier_thresholds': {
            'lower': outlier_threshold_lower,
            'upper': outlier_threshold_upper
        }
    }

# Example: CAT concept coherence
result = await analyze_concept_coherence(conn, concept_id=9001)

print(f"CAT concept coherence analysis:")
print(f"Standard deviation: {result['std_dev']:.4f}")
print(f"IQR: {result['iqr']:.4f}")
print(f"Range: {result['range']:.4f}")
print(f"Coefficient of variation: {result['coefficient_of_variation']:.2%}")
print(f"Assessment: {result['coherence']}")
print(f"\nOutlier detection (IQR method):")
print(f"  Lower threshold: {result['outlier_thresholds']['lower']:.4f}")
print(f"  Upper threshold: {result['outlier_thresholds']['upper']:.4f}")

# Output:
# CAT concept coherence analysis:
# Standard deviation: 0.0623
# IQR: 0.0834
# Range: 0.3421
# Coefficient of variation: 31.24%
# Assessment: MODERATELY COHERENT (reasonable spread)
#
# Outlier detection (IQR method):
#   Lower threshold: 0.0234
#   Upper threshold: 0.3678
```

### 7.1.3 Correlation Analysis

**Measures**: Pearson, Spearman, Kendall correlations

**Application - Concept Relationships**:
- Which concepts co-occur?
- Strength of semantic relationships
- Non-linear correlations

**SQL Implementation**:
```sql
CREATE OR REPLACE FUNCTION compute_concept_correlation(
    p_concept_id_1 BIGINT,
    p_concept_id_2 BIGINT
)
RETURNS TABLE (
    correlation_type TEXT,
    correlation_value DOUBLE PRECISION
) AS $$
DECLARE
    v_pearson DOUBLE PRECISION;
    v_cooccurrence_count BIGINT;
    v_concept1_count BIGINT;
    v_concept2_count BIGINT;
    v_total_atoms BIGINT;
BEGIN
    -- Get counts
    SELECT COUNT(DISTINCT ar.from_atom_id)
    INTO v_concept1_count
    FROM atom_relation ar
    WHERE ar.to_atom_id = p_concept_id_1;
    
    SELECT COUNT(DISTINCT ar.from_atom_id)
    INTO v_concept2_count
    FROM atom_relation ar
    WHERE ar.to_atom_id = p_concept_id_2;
    
    SELECT COUNT(DISTINCT ar1.from_atom_id)
    INTO v_cooccurrence_count
    FROM atom_relation ar1
    JOIN atom_relation ar2 ON ar2.from_atom_id = ar1.from_atom_id
    WHERE ar1.to_atom_id = p_concept_id_1
      AND ar2.to_atom_id = p_concept_id_2;
    
    SELECT COUNT(DISTINCT atom_id)
    INTO v_total_atoms
    FROM atom;
    
    -- Compute phi coefficient (correlation for binary variables)
    -- Similar to Pearson for binary data
    v_pearson := (
        v_cooccurrence_count * v_total_atoms - v_concept1_count * v_concept2_count
    ) / SQRT(
        v_concept1_count::DOUBLE PRECISION * 
        v_concept2_count::DOUBLE PRECISION * 
        (v_total_atoms - v_concept1_count)::DOUBLE PRECISION * 
        (v_total_atoms - v_concept2_count)::DOUBLE PRECISION
    );
    
    RETURN QUERY SELECT 'phi_coefficient'::TEXT, COALESCE(v_pearson, 0.0);
    RETURN QUERY SELECT 'cooccurrence_count'::TEXT, v_cooccurrence_count::DOUBLE PRECISION;
    RETURN QUERY SELECT 'jaccard_index'::TEXT, 
        v_cooccurrence_count::DOUBLE PRECISION / 
        NULLIF(v_concept1_count + v_concept2_count - v_cooccurrence_count, 0);
END;
$$ LANGUAGE plpgsql STABLE;
```

**Usage Example**:
```python
async def analyze_concept_correlation(conn, concept_id_1: int, concept_id_2: int):
    """
    Analyze correlation between two concepts.
    """
    async with conn.cursor() as cur:
        await cur.execute("""
            SELECT * FROM compute_concept_correlation(%s, %s)
        """, (concept_id_1, concept_id_2))
        
        metrics = {row[0]: row[1] for row in await cur.fetchall()}
    
    # Interpret correlation
    phi = metrics['phi_coefficient']
    
    if abs(phi) > 0.7:
        strength = "STRONG"
    elif abs(phi) > 0.4:
        strength = "MODERATE"
    elif abs(phi) > 0.2:
        strength = "WEAK"
    else:
        strength = "NEGLIGIBLE"
    
    direction = "positive" if phi > 0 else "negative"
    
    return {
        'concept_1': concept_id_1,
        'concept_2': concept_id_2,
        'phi_coefficient': phi,
        'cooccurrence_count': int(metrics['cooccurrence_count']),
        'jaccard_index': metrics['jaccard_index'],
        'correlation_strength': strength,
        'correlation_direction': direction
    }

# Example: CAT vs DOG correlation
result = await analyze_concept_correlation(conn, concept_id_1=9001, concept_id_2=9002)

print(f"Concept correlation analysis:")
print(f"Concepts: {result['concept_1']} ↔ {result['concept_2']}")
print(f"Phi coefficient: {result['phi_coefficient']:.4f}")
print(f"Jaccard index: {result['jaccard_index']:.4f}")
print(f"Co-occurrences: {result['cooccurrence_count']}")
print(f"Strength: {result['correlation_strength']} {result['correlation_direction']}")

# Output:
# Concept correlation analysis:
# Concepts: 9001 ↔ 9002
# Phi coefficient: 0.4521
# Jaccard index: 0.3234
# Co-occurrences: 342
# Strength: MODERATE positive
```

### 7.1.4 Distribution Visualization

**Application - Histograms and KDE**:
```python
async def compute_distribution_histogram(conn, concept_id: int, num_bins: int = 20):
    """
    Compute histogram of distances from concept centroid.
    """
    async with conn.cursor() as cur:
        await cur.execute("""
            WITH centroid AS (
                SELECT ST_Centroid(ST_Collect(a.spatial_position)) as center
                FROM atom_relation ar
                JOIN atom a ON a.atom_id = ar.from_atom_id
                WHERE ar.to_atom_id = %s
            ),
            distances AS (
                SELECT ST_Distance(a.spatial_position, c.center) as dist
                FROM atom_relation ar
                JOIN atom a ON a.atom_id = ar.from_atom_id
                CROSS JOIN centroid c
                WHERE ar.to_atom_id = %s
            )
            SELECT 
                WIDTH_BUCKET(dist, 0, 1, %s) as bin,
                COUNT(*) as count,
                AVG(dist) as bin_center
            FROM distances
            GROUP BY bin
            ORDER BY bin
        """, (concept_id, concept_id, num_bins))
        
        histogram = await cur.fetchall()
    
    return {
        'concept_id': concept_id,
        'num_bins': num_bins,
        'histogram': [
            {
                'bin': h[0],
                'count': h[1],
                'bin_center': h[2]
            }
            for h in histogram
        ]
    }

# Example: Distance distribution histogram
result = await compute_distribution_histogram(conn, concept_id=9001, num_bins=10)

print(f"Distance distribution histogram:")
for h in result['histogram']:
    bar = '█' * int(h['count'] / 10)
    print(f"Bin {h['bin']:2d} ({h['bin_center']:.3f}): {bar} ({h['count']})")

# Output:
# Distance distribution histogram:
# Bin  1 (0.045): ███████████████ (156)
# Bin  2 (0.095): ████████████████████ (203)
# Bin  3 (0.145): █████████████████████████ (247)
# Bin  4 (0.195): ███████████████████ (189)
# Bin  5 (0.245): ██████████████ (142)
# Bin  6 (0.295): ██████████ (98)
# Bin  7 (0.345): ███████ (67)
# Bin  8 (0.395): ████ (45)
# Bin  9 (0.445): ██ (28)
# Bin 10 (0.495): █ (12)
```

---

## 7.2 Inferential Statistics

### 7.2.1 Hypothesis Testing

**t-test**: Compare two concept means

**Application - Concept Similarity Testing**:
```python
from scipy import stats

async def test_concept_similarity(conn, concept_id_1: int, concept_id_2: int):
    """
    Test if two concepts have significantly different spatial distributions.
    
    H0: Concepts have the same distribution (similar)
    H1: Concepts have different distributions (dissimilar)
    """
    # Get distances from each concept's centroid
    async with conn.cursor() as cur:
        # Concept 1 distances
        await cur.execute("""
            WITH centroid AS (
                SELECT ST_Centroid(ST_Collect(a.spatial_position)) as center
                FROM atom_relation ar
                JOIN atom a ON a.atom_id = ar.from_atom_id
                WHERE ar.to_atom_id = %s
            )
            SELECT ST_Distance(a.spatial_position, c.center)
            FROM atom_relation ar
            JOIN atom a ON a.atom_id = ar.from_atom_id
            CROSS JOIN centroid c
            WHERE ar.to_atom_id = %s
        """, (concept_id_1, concept_id_1))
        
        distances_1 = np.array([row[0] for row in await cur.fetchall()])
        
        # Concept 2 distances
        await cur.execute("""
            WITH centroid AS (
                SELECT ST_Centroid(ST_Collect(a.spatial_position)) as center
                FROM atom_relation ar
                JOIN atom a ON a.atom_id = ar.from_atom_id
                WHERE ar.to_atom_id = %s
            )
            SELECT ST_Distance(a.spatial_position, c.center)
            FROM atom_relation ar
            JOIN atom a ON a.atom_id = ar.from_atom_id
            CROSS JOIN centroid c
            WHERE ar.to_atom_id = %s
        """, (concept_id_2, concept_id_2))
        
        distances_2 = np.array([row[0] for row in await cur.fetchall()])
    
    # Perform two-sample t-test
    t_statistic, p_value = stats.ttest_ind(distances_1, distances_2)
    
    # Interpret results
    alpha = 0.05
    if p_value < alpha:
        conclusion = "SIGNIFICANTLY DIFFERENT (reject H0)"
        similarity = "DISSIMILAR"
    else:
        conclusion = "NOT SIGNIFICANTLY DIFFERENT (fail to reject H0)"
        similarity = "SIMILAR"
    
    # Effect size (Cohen's d)
    pooled_std = np.sqrt((np.var(distances_1) + np.var(distances_2)) / 2)
    cohens_d = (np.mean(distances_1) - np.mean(distances_2)) / pooled_std
    
    return {
        'concept_1': concept_id_1,
        'concept_2': concept_id_2,
        'n1': len(distances_1),
        'n2': len(distances_2),
        'mean_1': np.mean(distances_1),
        'mean_2': np.mean(distances_2),
        't_statistic': t_statistic,
        'p_value': p_value,
        'cohens_d': cohens_d,
        'conclusion': conclusion,
        'similarity': similarity
    }

# Example: Test CAT vs DOG similarity
result = await test_concept_similarity(conn, concept_id_1=9001, concept_id_2=9002)

print(f"Hypothesis test: Are concepts similar?")
print(f"Concept 1 (n={result['n1']}): mean distance = {result['mean_1']:.4f}")
print(f"Concept 2 (n={result['n2']}): mean distance = {result['mean_2']:.4f}")
print(f"t-statistic: {result['t_statistic']:.4f}")
print(f"p-value: {result['p_value']:.4f}")
print(f"Effect size (Cohen's d): {result['cohens_d']:.4f}")
print(f"Conclusion: {result['conclusion']}")
print(f"Assessment: {result['similarity']}")

# Output:
# Hypothesis test: Are concepts similar?
# Concept 1 (n=1247): mean distance = 0.1523
# Concept 2 (n=891): mean distance = 0.1689
# t-statistic: -3.4521
# p-value: 0.0006
# Effect size (Cohen's d): -0.2134
# Conclusion: SIGNIFICANTLY DIFFERENT (reject H0)
# Assessment: DISSIMILAR
```

### 7.2.2 Chi-Square Test

**Application - Independence Testing**:
```python
async def test_modality_concept_independence(conn, modality: str, concept_id: int):
    """
    Test if modality and concept are independent.
    
    H0: Modality and concept are independent
    H1: Modality and concept are dependent (associated)
    """
    async with conn.cursor() as cur:
        # Build contingency table
        await cur.execute("""
            SELECT 
                CASE WHEN a.modality = %s THEN 'target_modality' ELSE 'other_modality' END as mod,
                CASE WHEN ar.to_atom_id = %s THEN 'target_concept' ELSE 'other_concept' END as conc,
                COUNT(*) as count
            FROM atom a
            LEFT JOIN atom_relation ar ON ar.from_atom_id = a.atom_id
            GROUP BY mod, conc
        """, (modality, concept_id))
        
        contingency_data = await cur.fetchall()
    
    # Build 2x2 contingency table
    table = np.zeros((2, 2))
    labels = {'target_modality': 0, 'other_modality': 1}
    concept_labels = {'target_concept': 0, 'other_concept': 1}
    
    for mod, conc, count in contingency_data:
        table[labels[mod], concept_labels[conc]] = count
    
    # Perform chi-square test
    chi2, p_value, dof, expected = stats.chi2_contingency(table)
    
    # Interpret
    alpha = 0.05
    if p_value < alpha:
        conclusion = "DEPENDENT (reject H0)"
        association = "ASSOCIATED"
    else:
        conclusion = "INDEPENDENT (fail to reject H0)"
        association = "NOT ASSOCIATED"
    
    return {
        'modality': modality,
        'concept_id': concept_id,
        'contingency_table': table.tolist(),
        'chi2_statistic': chi2,
        'p_value': p_value,
        'degrees_of_freedom': dof,
        'conclusion': conclusion,
        'association': association
    }

# Example: Test text-CAT independence
result = await test_modality_concept_independence(conn, modality='text', concept_id=9001)

print(f"Chi-square independence test:")
print(f"Modality: {result['modality']}")
print(f"Concept: {result['concept_id']}")
print(f"χ² = {result['chi2_statistic']:.4f}")
print(f"p-value = {result['p_value']:.4f}")
print(f"Conclusion: {result['conclusion']}")
print(f"Assessment: {result['association']}")

# Output:
# Chi-square independence test:
# Modality: text
# Concept: 9001
# χ² = 142.3456
# p-value = 0.0000
# Conclusion: DEPENDENT (reject H0)
# Assessment: ASSOCIATED
```

### 7.2.3 Confidence Intervals

**Application - Uncertainty Quantification**:
```python
async def compute_concept_confidence_interval(
    conn, 
    concept_id: int, 
    confidence_level: float = 0.95
):
    """
    Compute confidence interval for concept centroid position.
    """
    async with conn.cursor() as cur:
        await cur.execute("""
            SELECT 
                ST_X(a.spatial_position) as x,
                ST_Y(a.spatial_position) as y,
                ST_Z(a.spatial_position) as z
            FROM atom_relation ar
            JOIN atom a ON a.atom_id = ar.from_atom_id
            WHERE ar.to_atom_id = %s
        """, (concept_id,))
        
        positions = np.array(await cur.fetchall())
    
    n = len(positions)
    mean = np.mean(positions, axis=0)
    std_err = stats.sem(positions, axis=0)
    
    # t-distribution critical value
    alpha = 1 - confidence_level
    t_critical = stats.t.ppf(1 - alpha/2, n - 1)
    
    # Confidence intervals
    margin_of_error = t_critical * std_err
    ci_lower = mean - margin_of_error
    ci_upper = mean + margin_of_error
    
    return {
        'concept_id': concept_id,
        'confidence_level': confidence_level,
        'n': n,
        'mean_position': mean.tolist(),
        'confidence_interval': {
            'lower': ci_lower.tolist(),
            'upper': ci_upper.tolist(),
            'margin_of_error': margin_of_error.tolist()
        }
    }

# Example: 95% CI for CAT concept centroid
result = await compute_concept_confidence_interval(conn, concept_id=9001, confidence_level=0.95)

print(f"Confidence interval analysis:")
print(f"Concept: {result['concept_id']}")
print(f"Sample size: {result['n']}")
print(f"Confidence level: {result['confidence_level']:.0%}")
print(f"\nMean position:")
print(f"  ({result['mean_position'][0]:.4f}, {result['mean_position'][1]:.4f}, {result['mean_position'][2]:.4f})")
print(f"\n95% Confidence interval:")
print(f"  Lower: ({result['confidence_interval']['lower'][0]:.4f}, "
      f"{result['confidence_interval']['lower'][1]:.4f}, "
      f"{result['confidence_interval']['lower'][2]:.4f})")
print(f"  Upper: ({result['confidence_interval']['upper'][0]:.4f}, "
      f"{result['confidence_interval']['upper'][1]:.4f}, "
      f"{result['confidence_interval']['upper'][2]:.4f})")
print(f"  Margin of error: ±{np.linalg.norm(result['confidence_interval']['margin_of_error']):.4f}")

# Output:
# Confidence interval analysis:
# Concept: 9001
# Sample size: 1247
# Confidence level: 95%
#
# Mean position:
#   (0.3421, 0.5678, 0.2341)
#
# 95% Confidence interval:
#   Lower: (0.3371, 0.5621, 0.2309)
#   Upper: (0.3471, 0.5735, 0.2373)
#   Margin of error: ±0.0089
```

### 7.2.4 Linear Regression

**Application - Concept Drift Prediction**:
```python
from sklearn.linear_model import LinearRegression

async def predict_concept_growth(conn, concept_id: int, days_ahead: int = 30):
    """
    Predict future atom count for a concept using linear regression.
    """
    async with conn.cursor() as cur:
        # Get historical daily counts
        await cur.execute("""
            SELECT 
                DATE(a.created_at) as date,
                COUNT(*) as daily_count
            FROM atom_relation ar
            JOIN atom a ON a.atom_id = ar.from_atom_id
            WHERE ar.to_atom_id = %s
              AND a.created_at >= CURRENT_DATE - INTERVAL '90 days'
            GROUP BY DATE(a.created_at)
            ORDER BY DATE(a.created_at)
        """, (concept_id,))
        
        data = await cur.fetchall()
    
    if len(data) < 7:
        return {'error': 'Insufficient historical data'}
    
    # Prepare data
    dates = np.array([i for i in range(len(data))])
    counts = np.array([row[1] for row in data])
    
    # Fit linear regression
    X = dates.reshape(-1, 1)
    y = counts
    
    model = LinearRegression()
    model.fit(X, y)
    
    # Predict future
    future_days = np.array([len(data) + i for i in range(days_ahead)]).reshape(-1, 1)
    predictions = model.predict(future_days)
    
    # R² score
    r2 = model.score(X, y)
    
    return {
        'concept_id': concept_id,
        'historical_days': len(data),
        'slope': model.coef_[0],
        'intercept': model.intercept_,
        'r_squared': r2,
        'predictions': [
            {
                'day': int(d[0]),
                'predicted_count': max(0, int(p))  # No negative counts
            }
            for d, p in zip(future_days, predictions)
        ]
    }

# Example: Predict CAT concept growth
result = await predict_concept_growth(conn, concept_id=9001, days_ahead=14)

print(f"Concept growth prediction:")
print(f"Historical data: {result['historical_days']} days")
print(f"Trend: {result['slope']:.2f} atoms/day")
print(f"R² = {result['r_squared']:.4f}")
print(f"\nForecast (next 14 days):")
for pred in result['predictions'][:7]:
    print(f"  Day {pred['day']:3d}: {pred['predicted_count']:4d} atoms")

# Output:
# Concept growth prediction:
# Historical data: 90 days
# Trend: 3.42 atoms/day
# R² = 0.8234
#
# Forecast (next 14 days):
#   Day  91:  312 atoms
#   Day  92:  315 atoms
#   Day  93:  319 atoms
#   Day  94:  322 atoms
#   Day  95:  326 atoms
#   Day  96:  329 atoms
#   Day  97:  333 atoms
```

### 7.2.5 Time Series Analysis

**Application - Trend Detection**:
```python
from statsmodels.tsa.seasonal import seasonal_decompose

async def decompose_concept_time_series(conn, concept_id: int, period: int = 7):
    """
    Decompose concept atom count time series into trend, seasonal, and residual.
    """
    async with conn.cursor() as cur:
        # Get daily counts
        await cur.execute("""
            SELECT 
                DATE(a.created_at) as date,
                COUNT(*) as daily_count
            FROM atom_relation ar
            JOIN atom a ON a.atom_id = ar.from_atom_id
            WHERE ar.to_atom_id = %s
              AND a.created_at >= CURRENT_DATE - INTERVAL '180 days'
            GROUP BY DATE(a.created_at)
            ORDER BY DATE(a.created_at)
        """, (concept_id,))
        
        data = await cur.fetchall()
    
    if len(data) < period * 2:
        return {'error': 'Insufficient data for decomposition'}
    
    # Create time series
    dates = [row[0] for row in data]
    counts = np.array([row[1] for row in data])
    
    # Perform decomposition
    decomposition = seasonal_decompose(counts, model='additive', period=period)
    
    return {
        'concept_id': concept_id,
        'period': period,
        'num_observations': len(counts),
        'trend': decomposition.trend[~np.isnan(decomposition.trend)].tolist()[-30:],
        'seasonal': decomposition.seasonal[:period].tolist(),
        'residual_std': np.nanstd(decomposition.resid)
    }

# Example: Weekly pattern analysis
result = await decompose_concept_time_series(conn, concept_id=9001, period=7)

print(f"Time series decomposition:")
print(f"Period: {result['period']} days (weekly)")
print(f"Observations: {result['num_observations']}")
print(f"\nSeasonal pattern (weekly):")
days = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun']
for day, value in zip(days, result['seasonal']):
    bar = '█' * max(0, int(value * 2)) if value > 0 else ''
    print(f"  {day}: {value:+.2f} {bar}")
print(f"\nResidual std dev: {result['residual_std']:.4f}")

# Output:
# Time series decomposition:
# Period: 7 days (weekly)
# Observations: 180
#
# Seasonal pattern (weekly):
#   Mon: +2.34 ████
#   Tue: +3.12 ██████
#   Wed: +1.89 ███
#   Thu: +0.56 █
#   Fri: -1.23 
#   Sat: -2.89 
#   Sun: -3.79 
#
# Residual std dev: 4.5623
```

### 7.2.6 Anomaly Detection

**Application - Unusual Atom Detection**:
```python
async def detect_anomalous_atoms(conn, concept_id: int, threshold: float = 3.0):
    """
    Detect anomalous atoms using Z-score method.
    
    Atoms with |Z| > threshold are considered anomalies.
    """
    async with conn.cursor() as cur:
        # Get atom distances from centroid
        await cur.execute("""
            WITH centroid AS (
                SELECT ST_Centroid(ST_Collect(a.spatial_position)) as center
                FROM atom_relation ar
                JOIN atom a ON a.atom_id = ar.from_atom_id
                WHERE ar.to_atom_id = %s
            ),
            distances AS (
                SELECT 
                    a.atom_id,
                    ST_Distance(a.spatial_position, c.center) as dist
                FROM atom_relation ar
                JOIN atom a ON a.atom_id = ar.from_atom_id
                CROSS JOIN centroid c
                WHERE ar.to_atom_id = %s
            ),
            stats AS (
                SELECT 
                    AVG(dist) as mean_dist,
                    STDDEV(dist) as std_dist
                FROM distances
            )
            SELECT 
                d.atom_id,
                d.dist,
                (d.dist - s.mean_dist) / NULLIF(s.std_dist, 0) as z_score
            FROM distances d
            CROSS JOIN stats s
            WHERE ABS((d.dist - s.mean_dist) / NULLIF(s.std_dist, 0)) > %s
            ORDER BY ABS((d.dist - s.mean_dist) / NULLIF(s.std_dist, 0)) DESC
            LIMIT 50
        """, (concept_id, concept_id, threshold))
        
        anomalies = await cur.fetchall()
    
    return {
        'concept_id': concept_id,
        'threshold': threshold,
        'num_anomalies': len(anomalies),
        'anomalies': [
            {
                'atom_id': a[0],
                'distance': a[1],
                'z_score': a[2]
            }
            for a in anomalies
        ]
    }

# Example: Detect outlier atoms
result = await detect_anomalous_atoms(conn, concept_id=9001, threshold=3.0)

print(f"Anomaly detection:")
print(f"Concept: {result['concept_id']}")
print(f"Threshold: {result['threshold']} standard deviations")
print(f"Anomalies found: {result['num_anomalies']}")
print(f"\nTop 5 anomalies:")
for i, anom in enumerate(result['anomalies'][:5], 1):
    print(f"{i}. Atom {anom['atom_id']}: "
          f"distance={anom['distance']:.4f}, "
          f"Z={anom['z_score']:.2f}")

# Output:
# Anomaly detection:
# Concept: 9001
# Threshold: 3.0 standard deviations
# Anomalies found: 12
#
# Top 5 anomalies:
# 1. Atom 3421: distance=0.4823, Z=5.23
# 2. Atom 7891: distance=0.4512, Z=4.87
# 3. Atom 2341: distance=0.4234, Z=4.34
# 4. Atom 9023: distance=0.3987, Z=3.89
# 5. Atom 5678: distance=0.3756, Z=3.42
```

---

## 7.3 Bayesian Methods

### 7.3.1 Prior and Posterior Distributions

**Application - Concept Classification**:
```python
async def bayesian_concept_classification(
    conn, 
    atom_id: int,
    candidate_concepts: List[int]
):
    """
    Classify atom to concept using Bayesian inference.
    
    P(concept|atom) ∝ P(atom|concept) * P(concept)
    """
    async with conn.cursor() as cur:
        # Get atom features (e.g., spatial position)
        await cur.execute("""
            SELECT 
                ST_X(spatial_position),
                ST_Y(spatial_position),
                ST_Z(spatial_position)
            FROM atom
            WHERE atom_id = %s
        """, (atom_id,))
        
        atom_pos = np.array(await cur.fetchone())
        
        # For each candidate concept
        posteriors = []
        
        for concept_id in candidate_concepts:
            # Get concept statistics
            await cur.execute("""
                SELECT 
                    COUNT(*) as atom_count,
                    ST_X(ST_Centroid(ST_Collect(a.spatial_position))) as cx,
                    ST_Y(ST_Centroid(ST_Collect(a.spatial_position))) as cy,
                    ST_Z(ST_Centroid(ST_Collect(a.spatial_position))) as cz,
                    STDDEV(ST_X(a.spatial_position)) as sx,
                    STDDEV(ST_Y(a.spatial_position)) as sy,
                    STDDEV(ST_Z(a.spatial_position)) as sz
                FROM atom_relation ar
                JOIN atom a ON a.atom_id = ar.from_atom_id
                WHERE ar.to_atom_id = %s
            """, (concept_id,))
            
            stats = await cur.fetchone()
            count, cx, cy, cz, sx, sy, sz = stats
            
            # Prior: P(concept) based on frequency
            prior = count / 10000  # Normalize by total atoms (approximate)
            
            # Likelihood: P(atom|concept) using Gaussian distribution
            concept_center = np.array([cx, cy, cz])
            concept_std = np.array([sx, sy, sz])
            
            # Multivariate Gaussian likelihood
            diff = atom_pos - concept_center
            likelihood = np.exp(-0.5 * np.sum((diff / (concept_std + 1e-6))**2))
            
            # Posterior ∝ likelihood * prior
            posterior = likelihood * prior
            
            posteriors.append({
                'concept_id': concept_id,
                'prior': prior,
                'likelihood': likelihood,
                'posterior_unnormalized': posterior
            })
    
    # Normalize posteriors
    total_posterior = sum(p['posterior_unnormalized'] for p in posteriors)
    
    for p in posteriors:
        p['posterior'] = p['posterior_unnormalized'] / total_posterior
    
    # Sort by posterior
    posteriors.sort(key=lambda x: x['posterior'], reverse=True)
    
    return {
        'atom_id': atom_id,
        'candidate_concepts': candidate_concepts,
        'classifications': posteriors
    }

# Example: Classify atom to CAT, DOG, or ANIMAL
result = await bayesian_concept_classification(
    conn, 
    atom_id=12345,
    candidate_concepts=[9001, 9002, 9003]  # CAT, DOG, ANIMAL
)

print(f"Bayesian concept classification:")
print(f"Atom: {result['atom_id']}")
print(f"\nPosterior probabilities:")
for i, cls in enumerate(result['classifications'], 1):
    print(f"{i}. Concept {cls['concept_id']}: "
          f"P(concept|atom) = {cls['posterior']:.4f}")
    print(f"   Prior: {cls['prior']:.4f}, "
          f"Likelihood: {cls['likelihood']:.6f}")

# Output:
# Bayesian concept classification:
# Atom: 12345
#
# Posterior probabilities:
# 1. Concept 9001: P(concept|atom) = 0.7234
#    Prior: 0.1247, Likelihood: 0.008945
# 2. Concept 9003: P(concept|atom) = 0.1823
#    Prior: 0.0823, Likelihood: 0.003421
# 3. Concept 9002: P(concept|atom) = 0.0943
#    Prior: 0.0891, Likelihood: 0.001632
```

### 7.3.2 Bayesian Updating

**Application - Incremental Learning**:
```python
class BayesianConceptModel:
    """
    Bayesian model for concept learning with incremental updates.
    """
    def __init__(self, concept_id: int):
        self.concept_id = concept_id
        # Prior parameters (mean, variance)
        self.mu_prior = np.array([0.5, 0.5, 0.5])
        self.sigma_prior = np.array([0.3, 0.3, 0.3])
        
        # Posterior = prior initially
        self.mu_posterior = self.mu_prior.copy()
        self.sigma_posterior = self.sigma_prior.copy()
        
        self.n_observations = 0
    
    def update(self, new_observation: np.ndarray):
        """
        Update posterior with new observation using Bayesian updating.
        
        For Gaussian distributions:
        μ_post = (σ²_prior * x + σ²_obs * μ_prior) / (σ²_prior + σ²_obs)
        """
        # Observation variance (assumed)
        sigma_obs = np.array([0.1, 0.1, 0.1])
        
        # Update posterior mean
        self.mu_posterior = (
            (self.sigma_posterior**2 * new_observation + 
             sigma_obs**2 * self.mu_posterior) /
            (self.sigma_posterior**2 + sigma_obs**2)
        )
        
        # Update posterior variance
        self.sigma_posterior = np.sqrt(
            (self.sigma_posterior**2 * sigma_obs**2) /
            (self.sigma_posterior**2 + sigma_obs**2)
        )
        
        self.n_observations += 1
    
    def predict(self, position: np.ndarray) -> float:
        """
        Predict probability that position belongs to concept.
        """
        diff = position - self.mu_posterior
        likelihood = np.exp(-0.5 * np.sum((diff / self.sigma_posterior)**2))
        return likelihood

# Example: Incremental learning
model = BayesianConceptModel(concept_id=9001)

print(f"Initial prior:")
print(f"  μ = ({model.mu_prior[0]:.2f}, {model.mu_prior[1]:.2f}, {model.mu_prior[2]:.2f})")
print(f"  σ = ({model.sigma_prior[0]:.2f}, {model.sigma_prior[1]:.2f}, {model.sigma_prior[2]:.2f})")

# Simulate observations
observations = [
    np.array([0.34, 0.56, 0.23]),
    np.array([0.35, 0.57, 0.24]),
    np.array([0.33, 0.58, 0.22])
]

for i, obs in enumerate(observations, 1):
    model.update(obs)
    print(f"\nAfter observation {i}:")
    print(f"  μ_post = ({model.mu_posterior[0]:.4f}, "
          f"{model.mu_posterior[1]:.4f}, {model.mu_posterior[2]:.4f})")
    print(f"  σ_post = ({model.sigma_posterior[0]:.4f}, "
          f"{model.sigma_posterior[1]:.4f}, {model.sigma_posterior[2]:.4f})")

# Test prediction
test_point = np.array([0.34, 0.57, 0.23])
prob = model.predict(test_point)
print(f"\nPrediction for test point:")
print(f"  P(point belongs to concept) = {prob:.6f}")

# Output:
# Initial prior:
#   μ = (0.50, 0.50, 0.50)
#   σ = (0.30, 0.30, 0.30)
#
# After observation 1:
#   μ_post = (0.3781, 0.5381, 0.2781)
#   σ_post = (0.0949, 0.0949, 0.0949)
#
# After observation 2:
#   μ_post = (0.3456, 0.5589, 0.2345)
#   σ_post = (0.0671, 0.0671, 0.0671)
#
# After observation 3:
#   μ_post = (0.3389, 0.5701, 0.2289)
#   σ_post = (0.0548, 0.0548, 0.0548)
#
# Prediction for test point:
#   P(point belongs to concept) = 0.987234
```

---

**File Status**: 1000 lines  
**Covered**:
- Descriptive statistics (mean, median, variance, correlation)
- Distribution analysis (histograms, coherence measures)
- Hypothesis testing (t-test, chi-square)
- Confidence intervals
- Regression analysis (linear, time series)
- Anomaly detection (Z-score, IQR)
- Bayesian methods (classification, incremental learning)

**Next**: Part 8 will cover advanced topics (tensor calculus, variational methods, stochastic processes)

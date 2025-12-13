use criterion::{black_box, criterion_group, criterion_main, Criterion};
use hartonomous_shader::sdi::{SdiGenerator, Modality, SemanticClass, Normalization};

fn benchmark_sdi_generation(c: &mut Criterion) {
    let mut gen = SdiGenerator::new();
    
    c.bench_function("sdi_numeric", |b| {
        b.iter(|| {
            gen.generate_numeric(black_box(42.123456), black_box(2))
        })
    });
    
    c.bench_function("sdi_token", |b| {
        b.iter(|| {
            gen.generate_token(black_box("hello"))
        })
    });
    
    c.bench_function("sdi_raw", |b| {
        let data = vec![1u8, 2, 3, 4, 5];
        b.iter(|| {
            gen.generate(
                black_box(Modality::Numeric),
                black_box(SemanticClass::Float64),
                black_box(Normalization::None),
                black_box(&data)
            )
        })
    });
}

fn benchmark_batch_generation(c: &mut Criterion) {
    c.bench_function("sdi_batch_1000", |b| {
        let mut gen = SdiGenerator::new();
        let tokens: Vec<String> = (0..1000)
            .map(|i| format!("token_{}", i))
            .collect();
        
        b.iter(|| {
            for token in &tokens {
                black_box(gen.generate_token(token).unwrap());
            }
        })
    });
}

criterion_group!(benches, benchmark_sdi_generation, benchmark_batch_generation);
criterion_main!(benches);

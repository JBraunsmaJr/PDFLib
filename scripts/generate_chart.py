import pandas as pd
import matplotlib.pyplot as plt
import glob
import os

def generate_chart():
    # Find the latest CSV result file
    results_path = 'Benchmarks/BenchmarkDotNet.Artifacts/results/*-report.csv'
    files = glob.glob(results_path)
    if not files:
        print("No benchmark results found.")
        return

    latest_file = max(files, key=os.path.getmtime)
    print(f"Processing {latest_file}")

    df = pd.read_csv(latest_file)

    # Filter and process data
    # Assuming columns: Method, Mean, Allocated, FileName
    # We want to compare Methods (Dink vs PdfLib) for each FileName
    
    # Process Mean column
    if df['Mean'].dtype == object:
         df['Mean'] = df['Mean'].str.split(' ').str[0].str.replace(',', '')
    df['Mean'] = pd.to_numeric(df['Mean'], errors='coerce')

    # Process Allocated column
    if 'Allocated' in df.columns:
        if df['Allocated'].dtype == object:
             # Handle units if present (e.g., "1.5 KB", "100 B")
             def parse_alloc(val):
                 if pd.isna(val): return val
                 parts = str(val).split(' ')
                 if len(parts) < 2: return pd.to_numeric(parts[0].replace(',', ''), errors='coerce')
                 num = float(parts[0].replace(',', ''))
                 unit = parts[1].upper()
                 if unit == 'KB': return num * 1024
                 if unit == 'MB': return num * 1024 * 1024
                 if unit == 'GB': return num * 1024 * 1024 * 1024
                 return num # Bytes
             df['Allocated'] = df['Allocated'].apply(parse_alloc)
        else:
             df['Allocated'] = pd.to_numeric(df['Allocated'], errors='coerce')
    
    # Create subplots
    fig, (ax1, ax2) = plt.subplots(2, 1, figsize=(12, 12))

    # Plot Mean Execution Time
    pivot_mean = df.pivot(index='FileName', columns='Method', values='Mean')
    pivot_mean.plot(kind='bar', ax=ax1)
    ax1.set_title('Performance Comparison: Mean Execution Time')
    ax1.set_ylabel('Execution Time (ns/us/ms - check BDN output)')
    ax1.set_xlabel('Sample File')
    ax1.tick_params(axis='x', rotation=0)
    ax1.grid(axis='y', linestyle='--', alpha=0.7)
    ax1.legend(title='Library')

    # Plot Allocated Memory
    if 'Allocated' in df.columns:
        pivot_alloc = df.pivot(index='FileName', columns='Method', values='Allocated')
        # Convert to MB for better readability if values are large
        pivot_alloc = pivot_alloc / (1024 * 1024)
        pivot_alloc.plot(kind='bar', ax=ax2)
        ax2.set_title('Memory Comparison: Allocated Memory')
        ax2.set_ylabel('Allocated Memory (MB)')
        ax2.set_xlabel('Sample File')
        ax2.tick_params(axis='x', rotation=0)
        ax2.grid(axis='y', linestyle='--', alpha=0.7)
        ax2.legend(title='Library')
    else:
        ax2.text(0.5, 0.5, 'Allocated column not found in results', ha='center', va='center')

    plt.tight_layout()
    output_path = 'assets/overview.png'
    plt.savefig(output_path)
    print(f"Chart saved to {output_path}")

if __name__ == "__main__":
    generate_chart()

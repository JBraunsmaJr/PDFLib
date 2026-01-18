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
    # Assuming columns: Method, Mean, FileName
    # We want to compare Methods (Dink vs PdfLib) for each FileName
    
    if df['Mean'].dtype == object:
         df['Mean'] = df['Mean'].str.split(' ').str[0].str.replace(',', '')
    
    df['Mean'] = pd.to_numeric(df['Mean'], errors='coerce')
    
    pivot_df = df.pivot(index='FileName', columns='Method', values='Mean')

    ax = pivot_df.plot(kind='bar', figsize=(10, 6))
    plt.title('Performance Comparison: PDFLib vs DinkPdf')
    plt.ylabel('Mean Execution Time')
    plt.xlabel('Sample File')
    plt.xticks(rotation=0)
    plt.grid(axis='y', linestyle='--', alpha=0.7)
    plt.legend(title='Library')
    
    plt.tight_layout()
    output_path = 'assets/overview.png'
    plt.savefig(output_path)
    print(f"Chart saved to {output_path}")

if __name__ == "__main__":
    generate_chart()

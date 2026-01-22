import pandas as pd
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
import glob
import os

def generate_chart():
    # Find the latest CSV result file
    # Check both common locations
    search_paths = [
        'BenchmarkDotNet.Artifacts/results/*-report.csv',
        'Benchmarks/BenchmarkDotNet.Artifacts/results/*-report.csv'
    ]
    
    files = []
    for path in search_paths:
        files.extend(glob.glob(path))
        
    if not files:
        print(f"No benchmark results found in: {search_paths}")
        # Try to find any CSV in the current directory or subdirectories for debugging
        all_csvs = glob.glob('**/*.csv', recursive=True)
        print(f"Total CSV files found in project: {all_csvs}")
        exit(1)

    latest_file = max(files, key=os.path.getmtime)
    print(f"Processing {latest_file}")

    df = pd.read_csv(latest_file)
    
    # Check if we have data
    if df.empty:
        print("CSV file is empty.")
        return

    # Check for NA values in Mean
    if df['Mean'].isna().all() or (df['Mean'].dtype == object and (df['Mean'] == 'NA').all()):
        print("Warning: All 'Mean' values are NA. This usually means the benchmark failed or was terminated.")
        # We'll continue but the chart might be empty or broken

    # Filter and process data
    # Assuming columns: Method, Mean, Allocated, FileName
    # We want to compare Methods (Dink vs PdfLib) for each FileName

    # Debug: Print columns and first few rows
    print(f"Columns: {df.columns.tolist()}")
    print("First 5 rows of data before processing:")
    cols_to_print = [c for c in ['Method', 'FileName', 'Mean', 'Allocated'] if c in df.columns]
    print(df[cols_to_print].head())
    
    # Process Mean column
    if 'Mean' in df.columns:
        if df['Mean'].dtype == object:
             def parse_mean(val):
                 if pd.isna(val) or val == 'NA' or str(val).strip() == '-': return None
                 val_str = str(val).strip()
                 parts = val_str.split(' ')
                 if len(parts) < 2: 
                     # Try to see if unit is attached to the number
                     import re
                     match = re.match(r"([0-9,.]+)\s*([a-zA-Zμ]+)", val_str)
                     if match:
                         num_str, unit = match.groups()
                     else:
                         return pd.to_numeric(val_str.replace(',', ''), errors='coerce')
                 else:
                     num_str = parts[0]
                     unit = parts[1]
                 
                 try:
                     num = float(num_str.replace(',', ''))
                 except ValueError:
                     return None

                 unit = unit.lower()
                 if unit == 'ns': return num / 1000000.0
                 if unit == 'us' or unit == 'μs': return num / 1000.0
                 if unit == 'ms': return num
                 if unit == 's': return num * 1000.0
                 return num
             df['Mean'] = df['Mean'].apply(parse_mean)
        else:
             df['Mean'] = pd.to_numeric(df['Mean'], errors='coerce')
    else:
        print("Error: 'Mean' column not found in CSV!")
        # Try to find a column that might be Mean (sometimes BDN adds units to column name or similar)
        potential_mean = [c for c in df.columns if 'Mean' in c]
        if potential_mean:
            print(f"Found potential mean columns: {potential_mean}")
            df['Mean'] = pd.to_numeric(df[potential_mean[0]], errors='coerce')

    # Process Allocated column
    if 'Allocated' in df.columns:
        if df['Allocated'].dtype == object:
             # Handle units if present (e.g., "1.5 KB", "100 B")
             def parse_alloc(val):
                 if pd.isna(val) or val == 'NA' or str(val).strip() == '-': return None
                 val_str = str(val).strip()
                 parts = val_str.split(' ')
                 if len(parts) < 2: 
                     import re
                     match = re.match(r"([0-9,.]+)\s*([a-zA-Z]+)", val_str)
                     if match:
                         num_str, unit = match.groups()
                     else:
                         return pd.to_numeric(val_str.replace(',', ''), errors='coerce')
                 else:
                     num_str = parts[0]
                     unit = parts[1]
                 
                 try:
                     num = float(num_str.replace(',', ''))
                 except ValueError:
                     return None

                 unit = unit.upper()
                 if unit == 'B': return num
                 if unit == 'KB': return num * 1024
                 if unit == 'MB': return num * 1024 * 1024
                 if unit == 'GB': return num * 1024 * 1024 * 1024
                 return num # Default to Bytes
             df['Allocated'] = df['Allocated'].apply(parse_alloc)
        else:
             df['Allocated'] = pd.to_numeric(df['Allocated'], errors='coerce')

    print("First 5 rows of data after processing:")
    print(df[cols_to_print].head())
    
    # Create subplots
    try:
        fig, (ax1, ax2) = plt.subplots(2, 1, figsize=(12, 12))
        # Add a timestamp or run ID to the chart to ensure it always changes
        import datetime
        fig.suptitle(f'Benchmark Results - Generated at {datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")}')
    except Exception as e:
        print(f"Failed to create plots: {e}")
        return

    # Plot Mean Execution Time
    try:
        # Filter out NA values for plotting
        plot_df = df.dropna(subset=['Mean'])
        if plot_df.empty:
            print("No valid Mean values to plot.")
            ax1.text(0.5, 0.5, 'All Mean values are NA/missing', ha='center', va='center')
        else:
            pivot_mean = plot_df.pivot(index='FileName', columns='Method', values='Mean')
            # Sort index to ensure consistent order
            pivot_mean = pivot_mean.sort_index()
            pivot_mean.plot(kind='bar', ax=ax1)
            ax1.set_title('Performance Comparison: Mean Execution Time')
            ax1.set_ylabel('Execution Time')
            ax1.set_xlabel('Sample File')
            ax1.tick_params(axis='x', rotation=0)
            ax1.grid(axis='y', linestyle='--', alpha=0.7)
            ax1.legend(title='Library')
    except Exception as e:
        print(f"Error plotting Mean: {e}")
        ax1.text(0.5, 0.5, f'Error plotting Mean: {e}', ha='center', va='center')

    # Plot Allocated Memory
    if 'Allocated' in df.columns:
        try:
            # Filter out NA values for plotting
            plot_alloc_df = df.dropna(subset=['Allocated'])
            if plot_alloc_df.empty:
                print("No valid Allocated values to plot.")
                ax2.text(0.5, 0.5, 'All Allocated values are NA/missing', ha='center', va='center')
            else:
                pivot_alloc = plot_alloc_df.pivot(index='FileName', columns='Method', values='Allocated')
                # Sort index to ensure consistent order
                pivot_alloc = pivot_alloc.sort_index()
                # Convert to MB for better readability if values are large
                pivot_alloc = pivot_alloc / (1024 * 1024)
                pivot_alloc.plot(kind='bar', ax=ax2)
                ax2.set_title('Memory Comparison: Allocated Memory')
                ax2.set_ylabel('Allocated Memory (MB)')
                ax2.set_xlabel('Sample File')
                ax2.tick_params(axis='x', rotation=0)
                ax2.grid(axis='y', linestyle='--', alpha=0.7)
                ax2.legend(title='Library')
        except Exception as e:
            print(f"Error plotting Allocated: {e}")
            ax2.text(0.5, 0.5, f'Error plotting Allocated: {e}', ha='center', va='center')
    else:
        ax2.text(0.5, 0.5, 'Allocated column not found in results', ha='center', va='center')

    plt.tight_layout()
    output_path = 'assets/overview.png'
    plt.savefig(output_path)
    print(f"Chart saved to {output_path}")

if __name__ == "__main__":
    generate_chart()

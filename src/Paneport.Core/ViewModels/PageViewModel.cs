// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using CommunityToolkit.Mvvm.ComponentModel;
using Paneport.Core.Logging;

namespace Paneport.Core.ViewModels;

/// <summary>
/// The base of the pages that the shell shows.
/// </summary>
/// <remarks>
/// View models live on the UI thread. They await services without <c>ConfigureAwait(false)</c>,
/// so that they resume on the UI thread, where their properties are bound.
/// </remarks>
public abstract partial class PageViewModel : ObservableObject
{
    /// <summary>Creates the page.</summary>
    /// <param name="log">The log, which also receives load failures.</param>
    protected PageViewModel(ILog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        Log = log;
    }

    /// <summary>Gets the page's heading, which the shell shows above it.</summary>
    public abstract string Header { get; }

    /// <summary>Gets a value indicating whether the page is loading its data.</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>Gets a message for the user when the page could not load its data; otherwise null.</summary>
    [ObservableProperty]
    public partial string? LoadError { get; private set; }

    /// <summary>Gets the log.</summary>
    protected ILog Log { get; }

    /// <summary>Loads the page's data. The shell calls it when it first shows the page.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes when the page has loaded, or has recorded why it could not.</returns>
    /// <remarks>
    /// A failure sets <see cref="LoadError"/> and is logged with its exception type only, because
    /// an exception message can hold personal data such as a local path.
    /// </remarks>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        LoadError = null;
        try
        {
            await OnLoadAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LoadError = "This page could not load its data.";
            Log.Write(LogLevel.Error, GetType().Name, $"Loading failed with {exception.GetType().Name}.");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Loads the page's data. The default does nothing.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes when the data is loaded.</returns>
    protected virtual Task OnLoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

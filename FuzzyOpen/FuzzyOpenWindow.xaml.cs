﻿//------------------------------------------------------------------------------
// <copyright file="ToolWindow1Control.xaml.cs" company="Company">
//     Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace OpenDialogTest
{
	using EnvDTE;
	using Microsoft.VisualStudio.Shell.Interop;
	using System.Windows.Controls;
	using System;

	internal class cSuggestion : System.IComparable<cSuggestion>
	{
		internal cSuggestion(int matchlength, int matchposition, ProjectItem item)
		{
			mMatchLength = matchlength;
			mMatchPosition = matchposition;
			mItem = item;
			mNameLower = mItem.Name.ToLowerInvariant();
		}
		internal int mMatchLength;
		internal int mMatchPosition;
		internal string mNameLower;
		internal ProjectItem mItem;
		public override string ToString()
		{
			return mItem.Name;
		}

		public int CompareTo(cSuggestion other)
		{
			int ret = mMatchLength - other.mMatchLength;
			if (ret == 0) ret = mMatchPosition - other.mMatchPosition;
			return ret;
		}
		public string Name { get { return mItem.Name; } } 
		public string Path { get { return mItem.FileCount > 0 ? mItem.FileNames[0] : ""; } } 
	}

	public partial class TestWindow : System.Windows.Window
	{
		class cProjectItemWithMaskComparer : System.Collections.Generic.IEqualityComparer<cProjectItemWithMask>
		{
			public bool Equals(cProjectItemWithMask x, cProjectItemWithMask y)
			{
				return x.mItem == y.mItem;
			}

			public int GetHashCode(cProjectItemWithMask obj)
			{
				return obj.mItem.GetHashCode();
			}
		}
		class cProjectItemWithMask
		{
			public UInt64 mMask;
			public ProjectItem mItem;
			internal cProjectItemWithMask(ProjectItem item)
			{
				mMask = sGetMask(item.Name);
				mItem = item;
			}
			internal static UInt64 sGetMask(string name)
			{
				UInt64 mask = 0;
				foreach (char c in name)
				{
					int code = (int)System.Char.ToLower(c) - (int)'a';
					if(code < 64 && code >= 0) 
						mask |= (1u << code);
				}
				return mask;
			}
		}

		IVsUIShell mShell;
		System.Collections.Generic.HashSet<cProjectItemWithMask> mAllItems = new System.Collections.Generic.HashSet<cProjectItemWithMask>(new cProjectItemWithMaskComparer());
		public TestWindow(IVsUIShell shell)
		{
			mShell = shell;
			InitializeComponent();
			mLoadAllFiles();
			fileNamesGrid.MouseDoubleClick += ListBox_MouseDoubleClick;
			inputTextBox.TextChanged += TextBox_TextChanged;
			inputTextBox.KeyDown += TextBox_KeyDown;
			inputTextBox.PreviewKeyDown += TextBox_PreviewKeyDown;
		}

		private void TextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
		{
			if (e.Key == System.Windows.Input.Key.Down || (e.Key == System.Windows.Input.Key.Tab && e.KeyboardDevice.Modifiers == System.Windows.Input.ModifierKeys.None))
			{
				if (fileNamesGrid.SelectedIndex < ((System.Collections.Generic.List<cSuggestion>)fileNamesGrid.ItemsSource).Count)
					fileNamesGrid.SelectedIndex++;
				e.Handled = true;
			}
			else if (e.Key == System.Windows.Input.Key.Up || (e.Key == System.Windows.Input.Key.Tab && e.KeyboardDevice.Modifiers == System.Windows.Input.ModifierKeys.Shift))
			{
				if (fileNamesGrid.SelectedIndex > 0)
					fileNamesGrid.SelectedIndex--;
				e.Handled = true;
			}
			else if (e.Key == System.Windows.Input.Key.Escape)
				Close();
		}

		private void TextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
		{
			if (e.Key == System.Windows.Input.Key.Enter)
				mOpenSelectedSuggestion();
		}

		private void mOpenSelectedSuggestion()
		{
			var suggestions = (System.Collections.Generic.List<cSuggestion>)fileNamesGrid.ItemsSource;
			if (suggestions.Count > 0)
			{
				int index = fileNamesGrid.SelectedIndex;
				if (index == -1) index = 0;
				var item = suggestions[index].mItem;
				var window = item.Open(EnvDTE.Constants.vsViewKindCode);
				window.Visible = true;
				Close();
			}
		}

		private class cTaskInfo
		{
			public bool mDone = false;
			public string mPattern;
			public System.Threading.CancellationTokenSource mTokenSource = new System.Threading.CancellationTokenSource();
			public System.Collections.Generic.List<cSuggestion> mSuggestions = null;
		}
		private cTaskInfo mTaskInfo=null;

		private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			var newinfo = new cTaskInfo();
			var oldinfo=System.Threading.Interlocked.Exchange(ref mTaskInfo, newinfo);
			bool refresh=true;
			newinfo.mPattern = inputTextBox.Text;
			if (oldinfo != null)
			{
				oldinfo.mTokenSource.Cancel();
				if (oldinfo.mDone && newinfo.mPattern.StartsWith(oldinfo.mPattern))
				{
					newinfo.mSuggestions = oldinfo.mSuggestions;
					refresh = false;
				}
			}
			if(refresh)
			{
				UInt64 mask = cProjectItemWithMask.sGetMask(inputTextBox.Text);
				if (newinfo.mSuggestions != null) newinfo.mSuggestions.Clear();
				else newinfo.mSuggestions = new System.Collections.Generic.List<cSuggestion>();
				foreach(var pi in mAllItems)
					if((pi.mMask & mask) == mask)
						newinfo.mSuggestions.Add(new cSuggestion(0, 0, pi.mItem));
				if(newinfo.mPattern.Length<=1) // no point in doing more complicated fuzzing
				{
					fileNamesGrid.ItemsSource = newinfo.mSuggestions.GetRange(0, Math.Min(10, newinfo.mSuggestions.Count));
					fileNamesGrid.ItemsSource = newinfo.mSuggestions.GetRange(0, Math.Min(10, newinfo.mSuggestions.Count));
					fileNamesGrid.SelectedIndex = -1;
					newinfo.mDone = true;
					return;
				}
			}
			//System.Threading.Tasks.Task.Factory.StartNew(() => mGetSuggestions(text, list, this, newtokensource.Token));
			var token = newinfo.mTokenSource.Token;
			System.Threading.Tasks.Task.Run(() => mGetSuggestions(newinfo, this, token), newinfo.mTokenSource.Token);
		}

		private void mUpdateSuggestions(string pattern, System.Collections.Generic.List<cSuggestion> newsuggestions)
		{
				Dispatcher.Invoke(() =>
				{
					if (inputTextBox.Text == pattern)
					{
						fileNamesGrid.ItemsSource = newsuggestions.GetRange(0, Math.Min(10, newsuggestions.Count));
						fileNamesGrid.SelectedIndex = -1;
						fileNamesGrid.ItemsSource = newsuggestions.GetRange(0, Math.Min(10, newsuggestions.Count));
					}
				});
		}

		private static bool mGetMatch(string pattern, cSuggestion suggestion)
		{
			if (pattern == "") return true;
			int matchlength = 1;
			int matchstart = suggestion.mNameLower.IndexOf(pattern[0]);
			if (matchstart == -1 || matchstart + pattern.Length > suggestion.mNameLower.Length) return false;
			for(int i=1;i<pattern.Length;++i)
				for(;;)
				{
					char c = suggestion.mNameLower[matchlength + matchstart];
					if(c == pattern[i])
						break;
					if(i == 1 && c == pattern[0]) // change match start to this one for shortest match
					{
						matchstart = matchlength + matchstart;
						matchlength = 1;
					}
					else ++matchlength;
					if (matchlength + matchstart >= suggestion.mNameLower.Length)
						return false;
				}
			suggestion.mMatchLength = matchlength;
			suggestion.mMatchPosition = matchstart;
			return true;
		}

		private static void mGetSuggestions(cTaskInfo info, TestWindow w, System.Threading.CancellationToken token)
		{
			var ret = new System.Collections.Generic.List<cSuggestion>();
			for (int i = 0; i < info.mSuggestions.Count; ++i)
			{
				if (token.IsCancellationRequested) return;
				var sug = info.mSuggestions[i];
				if (mGetMatch(info.mPattern, sug))
					ret.Add(sug);
			}
			ret.Sort();
			info.mSuggestions = ret;
			info.mDone = true;
			w.mUpdateSuggestions(info.mPattern, ret);
		}

		private void ListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
		{
			mOpenSelectedSuggestion();
		}

		private void mLoadAllFiles()
		{
			var dte = FuzzyOpenPackage.GetGlobalService(typeof(DTE)) as EnvDTE80.DTE2;
			if (dte == null)
				System.Windows.MessageBox.Show("Could not get DTE object.");
			else
			{
				var solution = dte.Solution;
				if (!solution.IsOpen)
					System.Windows.MessageBox.Show("No solution currently open.");
				else
				{
					mAllItems.Clear();
					foreach (Project p in solution.Projects)
					{
						if(p.ProjectItems != null)
							foreach (ProjectItem pi in p.ProjectItems)
								GetFiles(pi, mAllItems);
					}
				}
			}
		}

		private void GetFiles(ProjectItem item, System.Collections.Generic.HashSet<cProjectItemWithMask> outitems)
		{
			if (System.Guid.Parse(item.Kind) == Microsoft.VisualStudio.VSConstants.ItemTypeGuid.PhysicalFile_guid)
				outitems.Add(new cProjectItemWithMask(item));
			if (item.ProjectItems != null)
				foreach (ProjectItem i in item.ProjectItems)
				{
					if (System.Guid.Parse(i.Kind) == Microsoft.VisualStudio.VSConstants.ItemTypeGuid.PhysicalFile_guid)
						outitems.Add(new cProjectItemWithMask(i));
					GetFiles(i, outitems);
				}
		}
	}
}
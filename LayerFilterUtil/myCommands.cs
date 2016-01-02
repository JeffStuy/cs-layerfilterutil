// (C) Copyright 2015 by  
//
using System;
using System.Collections.Generic;
using System.Text;

using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.LayerManager;
using System.Text.RegularExpressions;


// This line is not mandatory, but improves loading performances
[assembly: CommandClass(typeof(LayerFilterUtil.MyCommands))]

namespace LayerFilterUtil
{

	// This class is instantiated by AutoCAD for each document when
	// a command is called by the user the first time in the context
	// of a given document. In other words, non static data in this class
	// is implicitly per-document!
	public class MyCommands
	{

		// constants for the position of elements
		// in the argument passed
		private const int FUNCTION = 0;
		private const int FILTER_NAME = 1;
		private const int FILTER_TYPE = 2;
		private const int FILTER_PARENT = 3;
		private const int FILTER_EXPRESSION = 4;
		private const int FILTER_LAYERS = 4;
		private const int FILTER_MIN = 5;

		private static int nestDepth;

		private ResultBuffer resbufOut;

		private Document doc;
		private Database db;
		private Editor ed;


		[LispFunction("LayerFilterUtil")]

		public ResultBuffer LayerFilterUtil(ResultBuffer args) // This method can have any name
		{

			// initalize global vars
			doc = Application.DocumentManager.MdiActiveDocument;	// get reference to the current document
			db = doc.Database;										// get reference to the current dwg database
			ed = doc.Editor;											// get reference to the current editor (text window)

			resbufOut = new ResultBuffer();

			TypedValue[] tvArgs;

			// reset for each usage
			nestDepth = 0;

			// process the args buffer
			// if passed is null, no good return null
			if (args != null)
			{
				// convert the args buffer into an array
				tvArgs = args.AsArray();

				// if the first argument is not text
				// return null
				if (tvArgs[FUNCTION].TypeCode != (int)LispDataType.Text)
				{
					return null;
				}

				//displayArgs(tvArgs);
				//return null;

			}
			else
			{
				DisplayUsage();
				return null; ;
			}


			// access to the collection of layer filters
			LayerFilterTree lfTree = db.LayerFilters;
			LayerFilterCollection lfCollect = lfTree.Root.NestedFilters;

			switch (((string)tvArgs[FUNCTION].Value).ToLower())
			{
				case "list":
					// validate the args buffer, 2nd level - there can be only a single argument
					if (tvArgs.Length == 1)
					{
						return ListFilters(lfCollect);
					}

					return null;
					break;
				case "find":
					// finding 1 existing layer filter - did only 2 args get
					// provided and is the 2nd arg a text arg?  if yes, proceed
					if (tvArgs.Length == 2 && tvArgs[FILTER_NAME].TypeCode == (int)LispDataType.Text)
					{
						// search for the layer filter 
						return FindFilter(lfCollect, ((string)tvArgs[FILTER_NAME].Value));
					}

					return null;
					break;
				case "add":

					// add a new layer filter to the layer filter collection
					// allow the filter to be added as a nested filter to another filter
					// except that any filter that cannot be deleted, cannot have nested filters
					// parameter options:
					// first	(idx = 0 FUNCTION) parameter == "add"
					// second	(idx = 1 FILTER_NAME) parameter == "filter name" (cannot be duplicate)
					// third	(idx = 2 FILTER_TYPE) parameter == "filter type" either "property" or "group" (case does not matter)
					// fifth	(idx = 3 FILTER_PARENT) parameter == "parent name" or ("" or nil) for no parent name
					// fourth	(idx = 4 FILTER_EXPRESSION) parameter == "filter expression" for property filter
					// fourth	(idx = 4 FILTER_LAYERS) parameter == "layer ids" for a group filter
					

					// possible add options:
					// add a property filter to the root of the collection
					// add a property filter to another layer filter (property or group)
					// add a group filter to the root of the collection
					// add a group filter to to another group filter (cannot be a property filter)

					return AddFilter(lfTree, lfCollect, tvArgs);
					break;
				case "delete":
					// deleting existing layer filter(s) - only 2 args allowed
					// special case filter to delete = "*" means all of the existing filters
					// except those filters marked as "cannot delete"
					// provided and is the 2nd arg a text arg?  if yes, proceed

					if (tvArgs[FILTER_NAME].TypeCode == (int)LispDataType.Text)
					{
						string FilterName = (string)tvArgs[FILTER_NAME].Value;
						nestDepth = 0;

						if (FilterName != "*")
						{
							// delete one named filter
							//ed.WriteMessage("\n@1 name= " + (string)tvArgs[FILTER_NAME].Value);

							if (tvArgs.Length == 2)
							{
								List<LayerFilter> lFilters = SearchOneFilter(lfCollect, Name: FilterName, allowDelete: true);

								//ed.WriteMessage("\n@1.1"); 
								return DeleteOneFilter(lfTree, lfCollect, lFilters);
							}
						}
						else
						{
							// special case, name to delete is *
							// delete all filters that are not marked as cannot delete

							//ed.WriteMessage("\n@1");

							List<LayerFilter> lfList = SearchFilters(lfCollect, allowDelete: true);

							if (lfList != null && lfList.Count > 0)
							{
								//ed.WriteMessage("\n@100");
								return DeleteListOfFilters(lfTree, lfCollect, lfList);
							}
							
						}
					}

		
					resbufOut = null;
					break;
				case "usage":
					DisplayUsage();
					resbufOut = null;
					break;
			}

			return resbufOut;
		}

		/// <summary>
		/// List all of the layer filters
		/// </summary>
		/// <param name="lfCollect"></param>
		/// <returns></returns>
		private ResultBuffer ListFilters(LayerFilterCollection lfCollect)
		{
			return BuildResBuffer(SearchFilters(lfCollect));
		}


		/// <summary>
		/// Finds a single filter from the Layer Filter Collection<para />
		/// Returns a Result Buffer with the Layer Filter information
		/// </summary>
		/// <param name="lfCollect"></param>
		/// <param name="searchName"></param>
		/// <returns></returns>
		private ResultBuffer FindFilter(LayerFilterCollection lfCollect, string searchName)
		{
			List<LayerFilter> lFilter = SearchFilters(lfCollect, Name: searchName);

			if (lFilter.Count <= 0)
			{
				return null;
			}

			return BuildResBuffer(lFilter);
		}

		/// <summary>
		/// Add a filter
		/// </summary>
		/// <param name="lfTree"></param>
		/// <param name="lfCollect"></param>
		/// <returns></returns>
		private ResultBuffer AddFilter(LayerFilterTree lfTree, LayerFilterCollection lfCollect, TypedValue[] tvArgs)
		{

			// validate parameters
			// by getting to this point, first parameter validated


			// minimum of 5 parameters and 
			// parameters 1 & 2 must be text
			// parameter 3 must be text or nil
			// or new filter cannot already exist
			if (tvArgs.Length < FILTER_MIN
				|| tvArgs[FILTER_NAME].TypeCode != (int)LispDataType.Text
				|| tvArgs[FILTER_TYPE].TypeCode != (int)LispDataType.Text
				|| (tvArgs[FILTER_PARENT].TypeCode != (int)LispDataType.Text
				&& tvArgs[FILTER_PARENT].TypeCode != (int)LispDataType.Nil)
				|| SearchOneFilter(lfCollect, Name: (string)tvArgs[FILTER_NAME].Value) != null)
			{
				return null;
			}

			// parameter 4+ must be text
			for (int i = FILTER_EXPRESSION; i < tvArgs.Length; i++)
			{
				if (tvArgs[i].TypeCode != (int)LispDataType.Text)
					return null;
			}


			// *** parameters basic validation complete ***

			// at this point, we have the correct type of args (text or nil)
			switch (((string)tvArgs[FILTER_TYPE].Value).ToLower())
			{
				// add a property filter
				case "property":

					// two cases - add to root of tree or add to existing
					// if tvArgs[FILTER_PARENT] == "" or tvArgs[FILTER_PARENT] == nil, add to tree root
					// if tvArgs[FILTER_PARENT] == string, add to existing parent

					if (tvArgs[FILTER_PARENT].TypeCode == (int)LispDataType.Nil || ((string)tvArgs[FILTER_PARENT].Value).Length == 0)
					{
						// already checked that new filter does not exist - ok to proceed
						// add a property filter with the parent being null
						if (AddPropertyFilter(lfTree, lfCollect, (string)tvArgs[FILTER_NAME].Value, null, (string)tvArgs[FILTER_EXPRESSION].Value))
						{
							// filter added, return the data about the new filter
							return FindFilter(lfCollect, (string)tvArgs[FILTER_NAME].Value);
						}

					}
					else
					{
						// bit more complex - add a layer filter to an existing layer filter (nested layer filter)
						// parent filter must exist
						List<LayerFilter> lfList = SearchOneFilter(lfCollect, (string)tvArgs[FILTER_PARENT].Value);

						if (lfList != null)
						{
							// already checked that the new filter does not exist - ok to proceed
							// add a property filter using a parent
							if (AddPropertyFilter(lfTree, lfCollect, (string)tvArgs[FILTER_NAME].Value, lfList[0], (string)tvArgs[FILTER_EXPRESSION].Value))
							{
								// filter added, return data about the filter
								return FindFilter(lfCollect, (string)tvArgs[FILTER_NAME].Value);
							}
						}
					}

					// get here, something did not work - return nil
					return null;
					break;
				case "group":

					// two cases - have or have not parent

					if (tvArgs[FILTER_PARENT].TypeCode == (int)LispDataType.Nil || ((string)tvArgs[FILTER_PARENT].Value).Length == 0)
					{
						// simple case - add group filter to the tree root

						// args at this point:
						// FUNCTION = "add" - already verified
						// FILTER_NAME = filter name - already verified
						// FILTER_TYPE = "group" - already verified
						// FILTER_PARENT = filter parent is blank or nil - already verified
						// FILTER_LAYERS = begining of the list of layers to include in the group filter
						ObjectIdCollection layIds = new ObjectIdCollection();

						// store the aray of layers into a list
						List<string> layerNames = getLayersFromArg(tvArgs);

						if (layerNames.Count != 0)
						{
							// process the list of layers and get their layer ids
							layIds = getLayerIds(layerNames);

							if (layIds.Count != 0)
							{
								// now have a list of layer id's for the layer group
								// now add the layer filter group and its layer id's

								if (AddGroupFilter(lfTree, lfCollect,
									(string)tvArgs[FILTER_NAME].Value, null, layIds))
								{
									return FindFilter(lfCollect, (string)tvArgs[FILTER_NAME].Value);
								}
							}
						}
						// provide the return information
						return null;
						break;
					}
					else
					{
						// complex case - add group filter to an existing filter
						// existing filter cannot be a property filter

						// args at this point:
						// FUNCTION = "add" - already verified
						// FILTER_NAME = filter name - already verified (data type)
						// FILTER_TYPE = "group" - already verified
						// FILTER_PARENT = filter parent is not blank - already verified
						// FILTER_LAYERS = begining of the list of layers to include in the group filter

						List<LayerFilter> lfList = SearchOneFilter(lfCollect, (string)tvArgs[FILTER_PARENT].Value);

						// store the aray of layers into a list
						 List<string> layerNames = getLayersFromArg(tvArgs);

						if (layerNames.Count != 0 && lfList != null)
						{

							// process the list of layers and get their layer ids
							 ObjectIdCollection layIds = getLayerIds(layerNames);

							if (layIds.Count != 0)
							{
								// now have a list of layer id's for the layer group
								// now add the layer filter group and its layer id's

								if (AddGroupFilter(lfTree, lfCollect,
									(string)tvArgs[FILTER_NAME].Value, lfList[0], layIds))
								{
									return FindFilter(lfCollect, (string)tvArgs[FILTER_NAME].Value);
								}
							}
						}
						// provide the return information
						return null;
						break;
					}
					break;
			}
			return null;
		}



		/// <summary>
		/// Add a property filter
		/// </summary>
		/// <param name="lfTree">Filter Tree</param>
		/// <param name="lfCollect">Filter Collection</param>
		/// <param name="Name">New Filter Name</param>
		/// <param name="Parent">Existing Parent under which to add the new filter (null if add to root</param>
		/// <param name="Expression">The filter expression</param>
		/// <returns></returns>
		private bool AddPropertyFilter(LayerFilterTree lfTree, LayerFilterCollection lfCollect, 
			string Name, LayerFilter Parent, string Expression) 
		{

			if (Parent != null && Parent.AllowNested != true)
			{
				return false;
			}

			// the layer filter collection is lfCollect
			// since this may cause an error, use try catch
			try
			{
				// make an empty layer filter
				LayerFilter lf = new LayerFilter();

				// add the layer filter data
				lf.Name = Name;
				lf.FilterExpression = Expression;


				if (Parent == null)
				{
					// add the filter to the collection
					lfCollect.Add(lf);
				} 
				else 
				{
					// add the layer filter as a nested filter
					Parent.NestedFilters.Add(lf);
				}

				// add the collection back to the data base
				db.LayerFilters = lfTree;

				// update the layer palette to show the
				// layer filter changes
				refreshLayerManager();
			}
			catch (System.Exception)
			{
				// something did not work, return false
				return false;
			}

			return true;
		}

		/// <summary>
		/// Add a group filter
		/// </summary>
		/// <param name="lfTree"></param>
		/// <param name="lfCollect"></param>
		/// <param name="Name"></param>
		/// <param name="Parent"></param>
		/// <param name="layIds"></param>
		/// <returns></returns>
		private bool AddGroupFilter(LayerFilterTree lfTree, LayerFilterCollection lfCollect,
			string Name, LayerFilter Parent, ObjectIdCollection layIds )
		{
			// validate that this group filter is allowed to be added - 
			// if this is to be added to a Parent filter, the parent filter
			// must allow nesting
			// cannot be an ID (property) filter
			if (Parent != null && (Parent.AllowNested != true || Parent.IsIdFilter != true))
			{
				return false;
			}

			try
			{
				// create a blank layer filter group
				LayerGroup lg = new LayerGroup();

				// set its name
				lg.Name = Name;

				// add each layer id for the group
				foreach (ObjectId layId in layIds)
				{
					lg.LayerIds.Add(layId);
				}

				if (Parent == null)
				{
					// add the layer filter group to the collection
					lfCollect.Add(lg);
				}
				else
				{
					// add the layer filter as a nested filter
					Parent.NestedFilters.Add(lg);
				}

				// update the database with the updated tree
				db.LayerFilters = lfTree;

				// update the layer palette to show the
				// layer filter changes
				refreshLayerManager();
			}
			catch (System.Exception)
			{
				return false;
			}
			return true;
		}

		/// <summary>
		/// Delete the layer filter provided
		/// </summary>
		/// <param name="lfTree"></param>
		/// <param name="lfCollect"></param>
		/// <param name="lFilter"></param>
		/// <returns></returns>
		private bool DeleteFilter(LayerFilterTree lfTree, LayerFilterCollection lfCollect, LayerFilter lFilter)
		{
			// if the LayerFilter provided is null, return false
			if (lFilter == null)
			{
				return false;
			}

			//ed.WriteMessage("\n@7 name= " + lFilter.Name);
			//ed.WriteMessage("\n@7 parent= " + lFilter.Parent);


			// does this LayerFilter have a parent?
			if (lFilter.Parent == null)
			{
				// no - remove the filter from the root collection
				lfCollect.Remove(lFilter);
			}
			else
			{
				// yes - remove the filter from the parent's collection
				lFilter.Parent.NestedFilters.Remove(lFilter);
			}

			// write the updated layer filter tree back to the database
			db.LayerFilters = lfTree;

			// return success
			return true;
		}

		/// <summary>
		/// Delete one filter based on the name provided.  If the Filter
		/// does not exist or cannot be deleted, return null
		/// </summary>
		/// <param name="lfTree"></param>
		/// <param name="lfCollect"></param>
		/// <param name="searchName"></param>
		/// <returns></returns>
		private ResultBuffer DeleteOneFilter(LayerFilterTree lfTree, LayerFilterCollection lfCollect, List<LayerFilter> lFilters )
		{

			// if the list of layer filters found is null, no filters to delete, return null
			if (lFilters == null)
			{
				//ed.WriteMessage("\n@22");
				return null;
			}

			if (DeleteFilter(lfTree, lfCollect, lFilters[0]))
			{
				// update the layer palette to have the changes show up
				refreshLayerManager();
			}

			return BuildResBuffer(lFilters);
		}

		/// <summary>
		/// Deletes all of the filters in the List provided
		/// </summary>
		/// <param name="lfTree"></param>
		/// <param name="lfCollect"></param>
		/// <param name="lFilters"></param>
		/// <returns></returns>
		private ResultBuffer DeleteListOfFilters(LayerFilterTree lfTree, LayerFilterCollection lfCollect, List<LayerFilter> lFilters)
		{
			ResultBuffer resBuffer = new ResultBuffer();

			//ed.WriteMessage("\n@10");

			if (lFilters.Count == 0) { return resBuffer; }

			//ed.WriteMessage("\n@11 count= " + lFilters.Count);

			foreach (LayerFilter lFilter in lFilters)
			{
				//ed.WriteMessage("\n@12 name= " + lFilter.Name);
				//DeleteOneFilter(lfTree, lfCollect, lFilter.Name);
				DeleteFilter(lfTree, lfCollect, lFilter);
			}


			//ed.WriteMessage("@19\n");
			// return the list of not deleted filters
			return ListFilters(lfCollect);
		}


		/// <summary>
		/// Display the usage message
		/// </summary>
		private void DisplayUsage()
		{
			ed.WriteMessage("\nUsage:");
			ed.WriteMessage("\nDisplay Usage:");
			ed.WriteMessage("\n\t\t(layerFilterUtil) or");
			ed.WriteMessage("\n\t\t(layerFilterUtil \"usage\")");
			
			ed.WriteMessage("\nList all of the layer filters:");
			ed.WriteMessage("\n\t\t(layerFilterUtil \"list\")");
			
			ed.WriteMessage("\nFind a layer filter:");
			ed.WriteMessage("\n\t\t(layerFilterUtil \"find\" \"FilterNameToFind\")");
			
			ed.WriteMessage("\nAdd a top level property filter:");
			ed.WriteMessage("\n\t\t(layerFilterUtil \"add\" \"FilterNameToAdd\" \"Property\" nil \"Expression\")");
			
			ed.WriteMessage("\nAdd a property filter to an existing filter:");
			ed.WriteMessage("\n\t\t(layerFilterUtil \"add\" \"FilterNameToAdd\" \"Property\" \"ExistingFilterName\" \"Expression\")");
			
			ed.WriteMessage("\nAdd a top level group filter:");
			ed.WriteMessage("\n\t\t(layerFilterUtil \"add\" \"FilterNameToAdd\" \"Group\" nil \"LayerName\" \"LayerName\")");
			
			ed.WriteMessage("\nAdd a group filter to an existing filter:");
			ed.WriteMessage("\n\t\t(layerFilterUtil \"add\" \"FilterNameToAdd\" \"Group\" \"ExistingFilterName\" \"LayerName\" \"LayerName\")");
			
			ed.WriteMessage("\nDelete a layer filter:");
			ed.WriteMessage("\n\t\t(layerFilterUtil \"delete\" \"FilterNameToDelete\")");
	
			ed.WriteMessage("\nDelete all allowable layer filters:");
			ed.WriteMessage("\n\t\t(layerFilterUtil \"delete\" \"*\")");

			ed.WriteMessage("\nThe case of names does not matter except that, when a");
			ed.WriteMessage("\n\tfilter is added, the case of the name is used.");
			ed.WriteMessage("\nReturns a list when sucessful or nil when unsucessful");
		}

		/// <summary>
		/// Build the ResultBuffer based on a List of LayerFilters
		/// </summary>
		/// <param name="lFilters"></param>
		/// <returns></returns>
		private ResultBuffer BuildResBuffer(List<LayerFilter> lFilters)
		{
			ResultBuffer resBuffer = new ResultBuffer();

			// if nothing in the filter list, retrun null
			if (lFilters.Count <= 0)
			{
				return null;
			}

			// start the list
			resBuffer.Add(new TypedValue((int)LispDataType.ListBegin));

			// add the item count
			resBuffer.Add(
				new TypedValue((int)LispDataType.Int16, lFilters.Count));

			// begin the list of lists
			resBuffer.Add(new TypedValue((int)LispDataType.ListBegin));

			// add each list item
			foreach (LayerFilter lFilter in lFilters)
			{
				AddFilterToResBuffer(lFilter, resBuffer);
			}

			// end the list of lists
			resBuffer.Add(new TypedValue((int)LispDataType.ListEnd));

			// end the whole list
			resBuffer.Add(new TypedValue((int)LispDataType.ListEnd));

			return resBuffer;
		}

		/// <summary>
		/// Add a dotted pair to a ResultBuffer
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="dxfCode"></param>
		/// <param name="Value"></param>
		/// <param name="ResBuffer"></param>
		private void AddDottedPairToResBuffer<T>(int dxfCode, T Value, ResultBuffer ResBuffer)
		{
			// start with a list begin
			ResBuffer.Add(new TypedValue((int)LispDataType.ListBegin));

			// add the DXF code
			ResBuffer.Add(new TypedValue((int)LispDataType.Int16, dxfCode));

			// add the dotted pair value depending on the
			// type of TypedValue
			switch (Type.GetTypeCode(typeof(T)))
			{
				case TypeCode.String:
					ResBuffer.Add(
						new TypedValue((int)LispDataType.Text, Value));
					break;
				case TypeCode.Int16:
					ResBuffer.Add(
						new TypedValue((int)LispDataType.Int16, Value));
					break;
				case TypeCode.Int32:
					ResBuffer.Add(
						new TypedValue((int)LispDataType.Int32, Value));
					break;
				case TypeCode.Empty:
					ResBuffer.Add(
						new TypedValue((int)LispDataType.Text, ""));
					break;
			}

			// terminate the dotted pair
			ResBuffer.Add(new TypedValue((int)LispDataType.DottedPair));
		}

		/// <summary>
		/// Add a dotted pair to a ResultBuffer (overload for boolean values)
		/// </summary>
		/// <param name="dxfCode"></param>
		/// <param name="Value"></param>
		/// <param name="ResBuffer"></param>
		private void AddDottedPairToResBuffer(int dxfCode, bool Value, ResultBuffer ResBuffer)
		{
			// make a standard dotted pair by converting the boolean
			// to a short
			AddDottedPairToResBuffer(dxfCode, (short) (Value ? 1 : 0), ResBuffer);
		}

		/// <summary>
		/// Adds a layer filter to the ResultBufffer as a List of Dotted Pairs
		/// </summary>
		/// <param name="lFilter">The LayerFilter to Add</param>
		/// <param name="ResBuffer">The ResultBuffer in which to add the LayerFilter List</param>
		private void AddFilterToResBuffer(LayerFilter lFilter, ResultBuffer ResBuffer)
		{
			// DXF codes for the dotted pairs
			const int FILTERNAMEDXF = 300;
			const int FILTEREXPDXF = 301;
			const int FILTERPARENTDXF = 302;
			const int FILTERLAYERSDXF = 303;

			const int FILTERDELFLGDXF = 290;
			const int FILTERNESTFLGDXF = 291;
			const int FILTERGRPFLGDXF = 292;

			const int FILTERNESTCNTDXF = 90;

			ResBuffer.Add(new TypedValue((int)LispDataType.ListBegin));

			// 
			AddDottedPairToResBuffer(FILTERNAMEDXF, lFilter.Name, ResBuffer);

			// add either the layer expression dotted pair
			// of the list of layers dotted pair
			if (!lFilter.IsIdFilter)
			{
				// add the filter expression to the result buffer
				AddDottedPairToResBuffer(FILTEREXPDXF, lFilter.FilterExpression, ResBuffer);
			}
			else
			{
				// add the list of layers to the result buffer
				StringBuilder sb = new StringBuilder();

				using (Transaction tr = db.TransactionManager.StartTransaction())
				{
					// allocate for LayerTableRecord
					LayerTableRecord ltRecord;

					// iterate through all Layer Id's in the filter
					foreach (ObjectId layId in ((LayerGroup)lFilter).LayerIds)
					{
						// based on the layer Id, the the LayerTableRecord
						ltRecord = tr.GetObject(layId, OpenMode.ForRead) as LayerTableRecord;

						// add the layer name to the list with a trailing '/'
						sb.Append(ltRecord.Name + "/");
					}
				}

				// if the list of found layers is empty, create a "blank" entry
				if (sb.Length == 0) { sb = new StringBuilder("/", 1); }

				// have the formatted list of layers, add the dotted pair
				AddDottedPairToResBuffer(FILTERLAYERSDXF, sb.ToString(), ResBuffer);
			}

			// add dotted pair for the allow delete flag
			AddDottedPairToResBuffer(FILTERDELFLGDXF, lFilter.AllowDelete, ResBuffer);

			// add dotted pair for the parent name
			AddDottedPairToResBuffer(FILTERPARENTDXF,
				lFilter.Parent != null ? lFilter.Parent.Name : "",ResBuffer);

			// add dotted pair for the is id filter flag
			AddDottedPairToResBuffer(FILTERGRPFLGDXF, lFilter.IsIdFilter, ResBuffer);	// true = group filter; false = property filter

			// add dotted pair for the allow nested flag
			AddDottedPairToResBuffer(FILTERNESTFLGDXF, lFilter.AllowNested, ResBuffer);

			// add dotted pair for the nested filter count
			AddDottedPairToResBuffer(FILTERNESTCNTDXF, lFilter.NestedFilters.Count, ResBuffer);

			ResBuffer.Add(new TypedValue((int)LispDataType.ListEnd));
		}

		/// <summary>
		/// Search for LayerFilters based on the criteria provided
		/// </summary>
		/// <param name="lfCollect"></param>
		/// <param name="Name"></param>
		/// <param name="Parent"></param>
		/// <param name="allowDelete"></param>
		/// <param name="isGroup"></param>
		/// <param name="allowNested"></param>
		/// <param name="nestCount"></param>
		/// <returns></returns>
		private List<LayerFilter> SearchFilters(LayerFilterCollection lfCollect, 
			string Name = null, string Parent = null, 
			bool? allowDelete = null, bool? isGroup = null, 
			bool? allowNested = null, string nestCount = null)
		{
			// create the blank list (no elements - length == 0)
			List<LayerFilter> lfList = new List<LayerFilter>();

			if (lfCollect.Count == 0 || nestDepth > 100) { return lfList; }

			// prevent from getting too deep
			nestDepth++;


			//ed.WriteMessage("\n@6");
			//ed.WriteMessage("\n@6 - Name= " + Name);
			//ed.WriteMessage("\n@6 - Parent= " + Parent);
			//ed.WriteMessage("\n@6 - isGroup= " + isGroup);
			//ed.WriteMessage("\n@6 - allowDelete= " + allowDelete);
			//ed.WriteMessage("\n@6 - allowNested= " + allowNested);
			//ed.WriteMessage("\n@6 - nestcount= " + nestCount);


			foreach (LayerFilter lFilter in lfCollect)
			{
				//ed.WriteMessage("\n@61 name= " + lFilter.Name);
				if (ValidateFilter(lFilter, Name, Parent, allowDelete, isGroup, allowNested, nestCount)) {
					//ed.WriteMessage("\n@68");
					lfList.Add(lFilter);
				}

				if (lFilter.NestedFilters.Count != 0)
				{
					List<LayerFilter> lfListReturn = 
						SearchFilters(lFilter.NestedFilters, Name, Parent, allowDelete, isGroup, allowNested, nestCount);

					if (lfListReturn != null && lfListReturn.Count != 0)
					{
						lfList.AddRange(lfListReturn);
					}
				}
			}

			return lfList;

		}

		/// <summary>
		/// Search for one LayerFilter based on the criteria provided
		/// </summary>
		/// <param name="lfCollect"></param>
		/// <param name="Name"></param>
		/// <param name="Parent"></param>
		/// <param name="allowDelete"></param>
		/// <param name="isGroup"></param>
		/// <param name="allowNested"></param>
		/// <param name="nestCount"></param>
		/// <returns></returns>
		private List<LayerFilter> SearchOneFilter(LayerFilterCollection lfCollect,
			string Name = null, string Parent = null,
			bool? allowDelete = null, bool? isGroup = null,
			bool? allowNested = null, string nestCount = null)
		{

			//ed.WriteMessage("\n@51 name= " + Name);
			//ed.WriteMessage("\n@51 allowDelete= " + allowDelete);

			// create the list of LayerFilters
			List<LayerFilter> lFilters = new List<LayerFilter>(SearchFilters(lfCollect, Name, Parent, allowDelete, isGroup, allowNested, nestCount));

			//ed.WriteMessage("\n@55 count= " + lFilters.Count);

			// return LayerFilter if only 1 found, else return null
			return (lFilters.Count == 1 ? lFilters : null);
		}

		/// <summary>
		/// Based on the criteria provided, determine if the LayerFilter matches
		/// all criteria fields can be null - a null criteria indicates to not
		/// check that criteria item
		/// </summary>
		/// <param name="lFilter">LayerFilter to check</param>
		/// <param name="Name">Name criteria</param>
		/// <param name="Parent">Parent criteria</param>
		/// <param name="allowDelete">allowDelete flag criteria</param>
		/// <param name="isGroup">is group filter flag criteria</param>
		/// <param name="allowNested">allowNested flag criteria</param>
		/// <param name="nestCount">nestCount (as a string) criteria - 
		/// this requires a comparison operator: ==, !=, <, <=, >, >= </param>
		/// <returns></returns>

		private bool ValidateFilter(LayerFilter lFilter, 
			string Name = null, string Parent = null, 
			bool? allowDelete = null, bool? isGroup = null, 
			bool? allowNested = null, string nestCount = null)
		{

			//ed.WriteMessage("\n@3");
			//ed.WriteMessage("\n@3 - Name= " + Name);
			//ed.WriteMessage("\n@3 - Parent= " + Parent);
			//ed.WriteMessage("\n@3 - isGroup= " + isGroup);
			//ed.WriteMessage("\n@3 - allowDelete= " + allowDelete);
			//ed.WriteMessage("\n@3 - allowNested= " + allowNested);
			//ed.WriteMessage("\n@3 - nestcount= " + nestCount);

			// make easy tests first
			if (Name != null) { if (Name.Equals("") || !Name.Equals(lFilter.Name)) { return false; } }

			//ed.WriteMessage("\n@31");

			if (Parent != null) { if (Parent.Equals("") || !Parent.Equals(lFilter.Parent)) { return false; } }

			//ed.WriteMessage("\n@32");

			if (allowDelete != null) {if (allowDelete != lFilter.AllowDelete) {return false; } }

			//ed.WriteMessage("\n@33");

			if (isGroup != null) { if (isGroup != lFilter.IsIdFilter) { return false; } }

			//ed.WriteMessage("\n@34");

			if (allowNested != null) { if (allowNested != lFilter.AllowNested) { return false; } }

			//ed.WriteMessage("\n@35");

			// process nestCount
			// this allows for a conditional + a number to be
			// specified to determine a match
			if (nestCount != null && nestCount.Length > 1)
			{
				// setup for the nestCount check

				Match m = Regex.Match(nestCount,@"^(=|==|<=|>=|!=|<|>)\s*(\d+)");

				int nestCountValue;

				if (m.Success && int.TryParse(m.Groups[2].Value, out nestCountValue))
				{
					bool nestCountResult = false;

					switch (m.Groups[1].Value)
					{
						case "=":
						case "==":
							nestCountResult = lFilter.NestedFilters.Count == nestCountValue;
							break;
						case "<":
							nestCountResult = lFilter.NestedFilters.Count < nestCountValue;
							break;
						case "<=":
							nestCountResult = lFilter.NestedFilters.Count <= nestCountValue;
							break;
						case ">":
							nestCountResult = lFilter.NestedFilters.Count > nestCountValue;
							break;
						case ">=":
							nestCountResult = lFilter.NestedFilters.Count >= nestCountValue;
							break;
						case "!=":
							nestCountResult = lFilter.NestedFilters.Count != nestCountValue;
							break;
						default:
							nestCountResult = false;
							break;
					}

					if (!nestCountResult) { return false; }
				}
				else
				{
					// regex match failed or int.TryParse failed
					// cannot proceed - return false
					return false;
				}
			}

			//ed.WriteMessage("\n@39");
			// get here, all tests passed
			return true;
		}

		/// <summary>
		/// Created a List from the list of layers provided
		/// </summary>
		/// <param name="tvArgs">The Argument array</param>
		/// <returns></returns>
		private List<string> getLayersFromArg(TypedValue[] tvArgs)
		{
			List<string> layerNames = new List<string>();

			// if there are too few arguments (== no layers), return null
			if (FILTER_LAYERS > tvArgs.Length)
			{
				return layerNames;
			}


			// store the list of layers to add into a sorted list
			for (int i = FILTER_LAYERS; i < tvArgs.Length; i++)
			{
				layerNames.Add(((string)tvArgs[i].Value).ToLower());
			}

			return layerNames;
		}

		/// <summary>
		/// Create a collection of Object Id's (LayerId's)
		/// </summary>
		/// <param name="layerNames">A List of layerNames</param>
		/// <returns>A collection of LayerId's</returns>
		private ObjectIdCollection getLayerIds(List<string> layerNames)
		{
			ObjectIdCollection layIds = new ObjectIdCollection();

			if (layerNames.Count == 0)
			{
				return layIds;
			}

			// process the list of layers and place them into a sorted list
			// since working with a database item, use a transaction
			Transaction tr = db.TransactionManager.StartTransaction();

			using (tr)
			{
				LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

				foreach (ObjectId layId in lt)
				{
					LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(layId, OpenMode.ForRead);

					// check the name of the layer against the list of names to
					// add - if the key is contained, save the layer id and remove the
					// key from the add list
					if (layerNames.IndexOf(ltr.Name.ToLower()) >= 0)
					{
						layIds.Add(layId);
						layerNames.Remove(ltr.Name.ToLower());

						// if all of the layers get removed, done
						if (layerNames.Count == 0)
						{
							break;
						}
					}
				}

				tr.Commit();
			}

			return layIds;

		}

		/// <summary>
		/// Update the LayerManagerPalette so that the layer filter
		/// changes get displayed 
		/// </summary>
		private void refreshLayerManager()
		{
			object manager = Application.GetSystemVariable("LAYERMANAGERSTATE");

			// force a refresh of the layermanager palette to have
			// the changes show up
			if (manager.ToString().Contains("1"))
			{
				doc.SendStringToExecute("layerpalette ", true, false, false);
			}

		}

#if DEBUG
		/// <summary>
		/// List the information about the args passed to the command
		/// </summary>
		/// <param name="tvArgs">Array of args passed to the command</param>
		private void displayArgs(TypedValue[] tvArgs)
		{
			for (int i = 0; i < tvArgs.Length; i++)
			{
				ed.WriteMessage("arg#: " + i
					+ " : type: " + " \"" + describeLispDateType(tvArgs[i].TypeCode) + "\" (" + tvArgs[i].TypeCode + ")"
					+ " : value: >" + tvArgs[i].Value + "<");

				if (tvArgs[i].TypeCode == (short)LispDataType.Text)
				{
					ed.WriteMessage(" : length: " + ((string)tvArgs[i].Value).Length);
				}

				ed.WriteMessage("\n");

			}
		}

		/// <summary>
		/// Provide the description for the LispDataType
		/// </summary>
		/// <param name="tv">Short of the LispDataType</param>
		/// <returns></returns>
		private string describeLispDateType(short tv)
		{
			// todo - complete the below list
			switch (tv)
			{
				case (short)LispDataType.DottedPair:
					return "Dotted pair";
					break;
				case (short)LispDataType.Int16:
					return "Int16";
					break;
				case (short)LispDataType.Int32:
					return "Int32";
					break;
				case (short)LispDataType.ListBegin:
					return "ListBegin";
					break;
				case (short)LispDataType.ListEnd:
					return "ListEnd";
					break;
				case (short)LispDataType.None:
					return "None";
					break;
				case (short)LispDataType.Nil:
					return "Nil";
					break;
				case (short)LispDataType.Void:
					return "Dotted pair";
					break;
				case (short)LispDataType.Text:
					return "Text";
					break;
				default:
					return tv.ToString();

			}
		} 
#endif
	}
}

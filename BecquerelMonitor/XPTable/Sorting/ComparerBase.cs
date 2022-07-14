/*
 * Copyright � 2005, Mathew Hall
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or without modification, 
 * are permitted provided that the following conditions are met:
 *
 *    - Redistributions of source code must retain the above copyright notice, 
 *      this list of conditions and the following disclaimer.
 * 
 *    - Redistributions in binary form must reproduce the above copyright notice, 
 *      this list of conditions and the following disclaimer in the documentation 
 *      and/or other materials provided with the distribution.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND 
 * ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED 
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
 * IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, 
 * INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT 
 * NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
 * OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, 
 * WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
 * ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY 
 * OF SUCH DAMAGE.
 */


using System;
using System.Collections;
using System.Windows.Forms;

using XPTable.Models;


namespace XPTable.Sorting
{
	/// <summary>
	/// Base class for comparers used to sort the Cells contained in a TableModel
	/// </summary>
	public abstract class ComparerBase : IComparer
	{
		#region Class Data

		/// <summary>
		/// The TableModel that contains the Cells to be sorted
		/// </summary>
		private TableModel tableModel;

		/// <summary>
		/// The index of the Column to be sorted
		/// </summary>
		private int column;


		#endregion
		

		#region Constructor
		
		/// <summary>
		/// Initializes a new instance of the ComparerBase class with the specified 
		/// TableModel, Column index and SortOrder
		/// </summary>
		/// <param name="tableModel">The TableModel that contains the data to be sorted</param>
		/// <param name="column">The index of the Column to be sorted</param>
		/// <param name="sortOrder">Specifies how the Column is to be sorted</param>
		public ComparerBase(TableModel tableModel, int column, SortOrder sortOrder)
		{
			this.tableModel = tableModel;
			this.column = column;
		}

		#endregion


		#region Methods

        /// <summary>
        /// Compares two objects and returns a value indicating whether one is less 
        /// than, equal to or greater than the other.
        /// </summary>
        /// <param name="a">First object to compare</param>
        /// <param name="b">Second object to compare</param>
        /// <returns>-1 if a is less than b, 1 if a is greater than b, or 0 if a equals b</returns>
        public virtual int Compare(object a, object b)
        {
            if (!(a is Cell))
                throw new ArgumentException("Invalid type '" + a.GetType().FullName + "'. Cell expected.", "a");
            if (!(b is Cell))
                throw new ArgumentException("Invalid type '" + b.GetType().FullName + "'. Cell expected.", "b");

            Cell cell1 = (Cell)a;
            Cell cell2 = (Cell)b;

            // check for null cells
            if (cell1 == null && cell2 == null)
            {
                return 0;
            }
            else if (cell1 == null)
            {
                return -1;
            }
            else if (cell2 == null)
            {
                return 1;
            }

            int result = CompareCells(cell1, cell2);

            return result;
        }


        /// <summary>
        /// Compares two cells and returns a value indicating whether one is less 
        /// than, equal to or greater than the other.
        /// Both cells are non-null;
        /// </summary>
        /// <param name="cell1"></param>
        /// <param name="cell2"></param>
        /// <returns></returns>
        protected abstract int CompareCells(Cell cell1, Cell cell2);
		#endregion


		#region Properties

		/// <summary>
		/// Gets the TableModel that contains the Cells to be sorted
		/// </summary>
		public TableModel TableModel
		{
			get
			{
				return this.tableModel;
			}
		}


		/// <summary>
		/// Gets the index of the Column to be sorted
		/// </summary>
		public int SortColumn
		{
			get
			{
				return this.column;
			}
		}



		#endregion
	}
}

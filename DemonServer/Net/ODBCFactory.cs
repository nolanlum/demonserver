﻿/*
+---------------------------------------------------------------------------+
|	Demon - dAmn Emulator													|
|===========================================================================|
|	Copyright © 2008 Nol888													|
|===========================================================================|
|	This file is part of Demon.												|
|																			|
|	Demon is free software: you can redistribute it and/or modify			|
|	it under the terms of the GNU Affero General Public License as			|
|	published by the Free Software Foundation, either version 3 of the		|
|	License, or (at your option) any later version.							|
|																			|
|	This program is distributed in the hope that it will be useful,			|
|	but WITHOUT ANY WARRANTY; without even the implied warranty of			|
|	MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the			|
|	GNU Affero General Public License for more details.						|
|																			|
|	You should have received a copy of the GNU Affero General Public License|
|	along with this program.  If not, see <http://www.gnu.org/licenses/>.	|
|																			|
|===========================================================================|
|	> $Date: 2009-02-22 01:29:53 -0500 (Sun, 22 Feb 2009) $
|	> $Revision: 21 $
|	> $Author: nol888 $
+---------------------------------------------------------------------------+
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DemonServer.Net
{
	public sealed class ODBCFactory
	{
		private ODBCFactory() { }

		public static DBConn Instance
		{
			get
			{
				return ODBCFactory.Nested.connection;
			}
		}

		class Nested
		{
			static Nested() { }
			
			internal static readonly DBConn connection = new DBConn(ServerCore.MySQLHost, ServerCore.MySQLUsername, ServerCore.MySQLPassword, ServerCore.MySQLDatabase, ServerCore.MySQLPort);
		}
	}
}

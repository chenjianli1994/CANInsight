namespace PCAN_Client.UDS
{
    internal class NRC
    {
        public static string NRC_Deal(byte nrc)
        {
            string result = "";

            if ((0x01 <= nrc) && (0x0F >= nrc))
            {
                result = "ISO预留。用于以后扩展。";
            }
            else if ((0x15 <= nrc) && (0x20 >= nrc))
            {
                result = "ISO预留。用于以后扩展。";
            }
            else if ((0x27 <= nrc) && (0x30 >= nrc))
            {
                result = "ISO预留。用于以后扩展。";
            }
            else if ((0x38 <= nrc) && (0x4F >= nrc))
            {
                result = "预留。用于扩展数据链路安全。";
            }
            else if ((0x50 <= nrc) && (0x6F >= nrc))
            {
                result = "ISO预留。用于以后扩展。";
            }
            else if ((0x74 <= nrc) && (0x77 >= nrc))
            {
                result = "ISO预留。用于以后扩展。";
            }
            else if ((0x79 <= nrc) && (0x7D >= nrc))
            {
                result = "ISOSAEReserved";
            }
            else if ((0x94 <= nrc) && (0xEF >= nrc))
            {
                result = "预留。用于将来定义特定的条件不满足情况。";
            }
            else if ((0xF0 <= nrc) && (0xFE >= nrc))
            {
                result = "预留。用于车辆制造商定义特定的条件不满足的情况。";
            }
            else if ((0xFF <= nrc) && (0xFF >= nrc))
            {
                result = "ISO预留。用于以后扩展。";
            }
            else
            {
                switch (nrc)
                {
                    case 0x00:
                        result = "此编码用于服务器内部实现否定响应码的逻辑时使用，用于表示没有NRC，不给出否定响应。此编码不会在否定响应中给出。";
                        break;
                    case 0x10:
                        result = "表示请求的诊断服务被服务器(ECU)拒绝，但在本表中所有已定义的编码都不适用，这时回复此编码。";
                        break;
                    case 0x11:
                        result = "服务器不支持请求的诊断服务。诊断请求中的服务标识符(Service ID)是服务器不支持的或不能识别的，则服务器给出此NRC编码。";
                        break;
                    case 0x12:
                        result = "服务器支持诊断请求中的服务标识符(Service ID)，但不支持收到的子功能参数时，回复此编码。";
                        break;
                    case 0x13:
                        result = "请求服务的诊断报文中的数据长度与定义不一致时，回复此编码。\r\n请求服务中参数的格式与定义不一致时也会回复此编码。（不常用）";
                        break;
                    case 0x14:
                        result = "服务器准备给出的诊断响应中所包含的数据长度超出了服务器所支持的最大长度时，回复编码。";
                        break;
                    case 0x21:
                        result = "给出这个NRC编码时，表示服务器忙于执行已请求的诊断服务，暂时无法执行当前请求的诊断服务。";
                        break;
                    case 0x22:
                        result = "请求的诊断服务的执行条件不满足时，回复此编码。";
                        break;
                    case 0x23:
                        result = "ISO预留。用于以后扩展";
                        break;
                    case 0x24:
                        result = "请求服务的顺序不正确时，回复此编码。某写诊断服务请求是有先后顺序的。典型的就是安全访问(SecurityAccess)服务。必须先请求种子(Request Seed），再回复密钥(Send Key)。如果直接回复秘钥(Send Key)，则服务器会回复此编码。";
                        break;
                    case 0x25:
                        result = "此编码适用于网关。当向网关请求的服务需要子网段中的控制器执行去执行，但是子网段中的控制器没有正常的执行网关的请求。此时，网关应向请求诊断服务的设备回复此编码。";
                        break;
                    case 0x26:
                        result = "由于当前服务器存在故障，并且已经记录下了对应的故障码(DTC)，切此故障会导致请求的服务无法执行时，回复此编码。";
                        break;
                    case 0x31:
                        result = "诊断请求中的参数超出定义的范围，或者访问的数据标识符(DID)、例程标识符(RoutineID)是服务器不支持或在当前会话不支持时，回复此编码。";
                        break;
                    case 0x32:
                        result = "ISO预留。用于以后扩展";
                        break;
                    case 0x33:
                        result = "通常在所请求的诊断服务需要服务器处于解锁状态，但服务器未被解锁时，回复此编码。";
                        break;
                    case 0x34:
                        result = "ISO预留。用于以后扩展。";
                        break;
                    case 0x35:
                        result = "服务器收到的安全访问(SecurityAccess)服务请求子功能为发送秘钥(SendKey)，但服务器收到的秘钥(Key)不正确时，回复此编码。";
                        break;
                    case 0x36:
                        result = "请求安全访问(SecurityAccess)服务的失败次数超过服务器允许的最大次数时，回复此编码。";
                        break;
                    case 0x37:
                        result = "服务器在安全访问延迟时间内收到安全访问(SecurityAccess)服务请求时，回复此编码。";
                        break;
                    case 0x70:
                        result = "由于故障导致从服务器的存储器上传数据失败或向服务器的存储器下载数据失败时，回复此代码。";
                        break;
                    case 0x71:
                        result = "由于故障导致数据传输操作被中断时，回复此编码。";
                        break;
                    case 0x72:
                        result = "服务器在擦除或写入Flash出现错误时，回复此代码。";
                        break;
                    case 0x73:
                        result = "在执行数据传输服务(TransferData (0x36) service)的过程中，检测到数据块序列编号(BlockSequenceCounter)错误时，回复此编码。";
                        break;
                    case 0x78:
                        result = "诊断请求已经收到，并且是有效的，服务器正在执行请求的服务，无法继续接收新的服务请求时，回复此代码。当正在执行的服务完成后，仍需给出最终的肯定或否定响应。";
                        break;
                    case 0x7E:
                        result = "诊断请求中服务的子功能参数在当前的会话下不支持时，回复此编码。需要注意的是，回复此编码时，子功能参数是服务器在其它会话下支持的，只是在当前的会话下不支持。如果服务器在任何会话下都不支持此子功能参数，则需回复0x12.";
                        break;
                    case 0x7F:
                        result = "诊断请求中的服务标识符(Service ID)在当前的会话下不支持时，回复此编码。需要注意的是，回复此编码时，的服务标识符(Service ID)是服务器在其它会话下支持的，只是在当前的会话下不支持。如果服务器在任何会话下都不支持此子功能参数，则需回复0x11.";
                        break;
                    case 0x80:
                        result = "ISO预留。用于以后扩展。";
                        break;
                    case 0x81:
                        result = "请求的诊断服务被执行的条件之一是发动机转速低于某一限值，而此时的发动机转速不满足此要求时，回复此编码。";
                        break;
                    case 0x82:
                        result = "请求的诊断服务被执行的条件之一是发动机转速高于某一限值，而此时的发动机转速不满足此要求时，回复此编码。";
                        break;
                    case 0x83:
                        result = "请求的诊断服务被执行的条件之一是发动机处于停机状态，而此时发动机处于运转状态，则回复此编码。";
                        break;
                    case 0x84:
                        result = "请求的诊断服务被执行的条件之一是发动机处于运转状态，而此时发动机处于停机状态，则回复此编码。";
                        break;
                    case 0x85:
                        result = "请求的诊断服务被执行的条件之一是发动机运转的时间超过某一限值，而此时该条件不满足，则回复此编码。";
                        break;
                    case 0x86:
                        result = "请求的诊断服务被执行的条件之一是当前的温度低于某一限值，而此时该条件不满足，则回复此编码。";
                        break;
                    case 0x87:
                        result = "请求的诊断服务被执行的条件之一是当前的温度高于某一限值，而此时该条件不满足，则回复此编码。";
                        break;
                    case 0x88:
                        result = "请求的诊断服务被执行的条件之一是当前的车速低于某一限值，而此时该条件不满足，则回复此编码。";
                        break;
                    case 0x89:
                        result = "请求的诊断服务被执行的条件之一是当前的车速高于某一限值，而此时该条件不满足，则回复此编码。";
                        break;
                    case 0x8A:
                        result = "请求的诊断服务被执行的条件之一是节气门开度或加速踏板开度低于某一限值，而此时该条件不满足，则回复此编码。";
                        break;
                    case 0x8B:
                        result = "请求的诊断服务被执行的条件之一是节气门开度或加速踏板开度高于某一限值，而此时该条件不满足，则回复此编码。";
                        break;
                    case 0x8C:
                        result = "请求的诊断服务被执行的条件之一是变速器处于空档，而此时该条件不满足，则回复此编码。";
                        break;
                    case 0x8D:
                        result = "请求的诊断服务被执行的条件之一是变速器处于非空档，而此时该条件不满足，则回复此编码。";
                        break;
                    case 0x8E:
                        result = "ISO预留。用于以后扩展。";
                        break;
                    case 0x8F:
                        result = "请求的诊断服务被执行的条件之一是在诊断服务被执行前和执行过程中制动踏板没有被踩下，而此时该条件不满足，则回复此编码。";
                        break;
                    case 0x90:
                        result = "请求的诊断服务被执行的条件之一是变速器处于P空档，而此时该条件不满足，则回复此编码。";
                        break;
                    case 0x91:
                        result = "请求的诊断服务被执行的条件之一是液力变矩器未处于锁止状态，而此时该条件不满足，则回复此编码。";
                        break;
                    case 0x92:
                        result = "请求的诊断服务被执行的条件之一是蓄电池电压低于设定的限值，而此时该条件不满足，则回复此编码。";
                        break;
                    case 0x93:
                        result = "请求的诊断服务被执行的条件之一是蓄电池电压高于设定的限值，而此时该条件不满足，则回复此编码。";
                        break;

                }
            }
            return result;
        }
    }
}
